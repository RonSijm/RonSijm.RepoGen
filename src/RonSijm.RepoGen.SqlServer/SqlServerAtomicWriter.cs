using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.SqlServer;

/// <summary>SQL Server insert-if-absent with a serializable update-locked unique-key probe and OUTPUT INSERTED.</summary>
/// <remarks>The caller supplies an operation transaction. No MERGE, duplicate-error swallowing, or implicit retry is used.</remarks>
[RepositoryProvider("Microsoft.EntityFrameworkCore.SqlServer")]
public sealed class SqlServerAtomicWriter : IRepositoryAtomicWriter
{
    public async Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            throw new NotSupportedException("SqlServerAtomicWriter requires SQL Server.");
        }

        var metadata = context.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException("The atomic entity is not mapped.");
        ValidateModel(metadata);
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Atomic insert requires the operation transaction.");
        }

        var table = metadata.GetTableName()!;
        var schema = metadata.GetSchema();
        var store = StoreObjectIdentifier.Table(table, schema);
        var properties = metadata.GetProperties().ToArray();
        var insert = columns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown insert property.", nameof(columns))).ToArray();
        var conflicts = conflictColumns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown conflict property.", nameof(conflictColumns))).ToArray();
        if (insert.Length == 0 || insert.Distinct().Count() != insert.Length || conflicts.Length == 0 || conflicts.Distinct().Count() != conflicts.Length ||
            conflicts.Any(property => property.IsNullable || !insert.Contains(property) || property.PropertyInfo!.GetValue(entity) is null) ||
            !metadata.GetKeys().Any(key => key.Properties.SequenceEqual(conflicts)) &&
            !metadata.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.SequenceEqual(conflicts)))
        {
            throw new InvalidOperationException("Atomic conflict columns must be supplied non-null properties of a declared primary/unique key or unfiltered unique index.");
        }

        if (insert.Any(property => property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save ||
                                   property.ValueGenerated == ValueGenerated.OnAddOrUpdate || property.GetValueGenerationStrategy(store) == SqlServerValueGenerationStrategy.IdentityColumn))
        {
            throw new InvalidOperationException("Atomic insert cannot assign identity, ignored, computed or rowversion columns.");
        }

        var helper = context.GetService<ISqlGenerationHelper>();
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        var target = helper.DelimitIdentifier(table, schema);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (context.Database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        for (var index = 0; index < insert.Length; index++)
        {
            command.Parameters.Add(insert[index].GetRelationalTypeMapping().CreateParameter(command, "@p" + index,
                insert[index].PropertyInfo!.GetValue(entity), insert[index].IsNullable));
        }

        command.CommandText = "INSERT INTO " + target + " (" + string.Join(", ", insert.Select(Column)) + ") OUTPUT " +
                              string.Join(", ", properties.Select(property => "INSERTED." + Column(property))) + " SELECT " +
                              string.Join(", ", Enumerable.Range(0, insert.Length).Select(static index => "@p" + index)) +
                              " WHERE NOT EXISTS (SELECT 1 FROM " + target + " WITH (UPDLOCK, HOLDLOCK) WHERE " +
                              string.Join(" AND ", conflicts.Select(property => Column(property) + " = @p" + Array.IndexOf(insert, property))) + ");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new(CommandStatus.AlreadyExists);
        }

        var values = new object?[properties.Length];
        for (var index = 0; index < properties.Length; index++)
        {
            if (await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var mapping = properties[index].GetRelationalTypeMapping();
            var value = mapping.GetDataReaderMethod().Invoke(reader, [index]);
            values[index] = mapping.Converter is null ? value : mapping.Converter.ConvertFromProvider(value);
        }

        // Drain the statement before reporting success: OUTPUT may deliver rows before a later SQL error.
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Atomic insertion returned more than one row.");
        }

        while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
        }

        for (var index = 0; index < properties.Length; index++)
        {
            properties[index].PropertyInfo!.SetValue(entity, values[index]);
        }

        return new(CommandStatus.Inserted, 1, entity, hasValue: true);
    }

    /// <summary>Validate the supported scalar table shape without acquiring a connection.</summary>
    public static void ValidateModel(IEntityType metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var table = metadata.GetTableName();
        var schema = metadata.GetSchema();
        if (table is null || metadata.FindPrimaryKey() is null || metadata.BaseType is not null || metadata.GetDerivedTypes().Any() ||
            metadata.IsOwned() || metadata.HasSharedClrType || metadata.GetTableMappings().Count() != 1 ||
            metadata.GetDeclaredTriggers().Any() || metadata.IsMemoryOptimized() ||
            metadata.Model.GetEntityTypes().Any(other => other != metadata && other.GetTableName() == table && other.GetSchema() == schema))
        {
            throw new NotSupportedException("Atomic SQL Server writes require one ordinary, unshared, non-inherited table without triggers or memory optimization.");
        }

        var store = StoreObjectIdentifier.Table(table, schema);
        if (metadata.GetProperties().Any(property => property.PropertyInfo?.SetMethod is null || property.GetColumnName(store) is null))
        {
            throw new NotSupportedException("Atomic returned values require mapped CLR scalar properties with setters; shadow/backing-field-only mappings are unsupported.");
        }
    }
}