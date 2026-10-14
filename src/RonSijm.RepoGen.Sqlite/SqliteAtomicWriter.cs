using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sqlite;

/// <summary>SQLite INSERT ON CONFLICT DO NOTHING RETURNING, protected by a declared non-null, unfiltered unique key.</summary>
[RepositoryProvider("Microsoft.EntityFrameworkCore.Sqlite")]
public sealed class SqliteAtomicWriter : IRepositoryAtomicWriter
{
    public async Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new NotSupportedException("SqliteAtomicWriter requires SQLite.");
        }

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Atomic insert requires the operation transaction.");
        }

        var metadata = context.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException("The atomic entity is not mapped.");
        var table = metadata.GetTableName();
        if (table is null || metadata.GetSchema() is not null || metadata.BaseType is not null || metadata.GetDerivedTypes().Any() ||
            metadata.IsOwned() || metadata.HasSharedClrType || metadata.GetTableMappings().Count() != 1)
        {
            throw new NotSupportedException("Atomic SQLite writes require a single ordinary non-inherited table mapping without a schema.");
        }

        var store = StoreObjectIdentifier.Table(table, null);
        var properties = metadata.GetProperties().ToArray();
        if (properties.Any(static property => property.PropertyInfo?.SetMethod is null))
        {
            throw new NotSupportedException("Atomic returned values require mapped CLR scalar properties with setters; shadow/backing-field-only mappings are unsupported.");
        }

        var insert = columns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown insert property.", nameof(columns))).ToArray();
        var conflicts = conflictColumns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown conflict property.", nameof(conflictColumns))).ToArray();
        if (insert.Length == 0 || insert.Distinct().Count() != insert.Length || conflicts.Length == 0 || conflicts.Distinct().Count() != conflicts.Length ||
            conflicts.Any(property => property.IsNullable || !insert.Contains(property) || property.PropertyInfo!.GetValue(entity) is null) ||
            !metadata.GetKeys().Any(key => key.Properties.SequenceEqual(conflicts)) && !metadata.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.SequenceEqual(conflicts)))
        {
            throw new InvalidOperationException("Atomic conflict columns must be supplied non-null properties of a declared primary/unique key or unfiltered unique index.");
        }

        if (insert.Any(static property => property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save || property.ValueGenerated == ValueGenerated.OnAddOrUpdate))
        {
            throw new InvalidOperationException("Atomic insert cannot assign ignored/computed/store-generated-on-update columns.");
        }

        var helper = context.GetService<ISqlGenerationHelper>();
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (context.Database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        for (var index = 0; index < insert.Length; index++)
        {
            command.Parameters.Add(insert[index].GetRelationalTypeMapping().CreateParameter(command, "@p" + index, insert[index].PropertyInfo!.GetValue(entity), insert[index].IsNullable));
        }

        command.CommandText = "INSERT INTO " + helper.DelimitIdentifier(table) + " (" + string.Join(", ", insert.Select(Column)) + ") VALUES (" +
                              string.Join(", ", Enumerable.Range(0, insert.Length).Select(static index => "@p" + index)) + ") ON CONFLICT (" +
                              string.Join(", ", conflicts.Select(Column)) + ") DO NOTHING RETURNING " + string.Join(", ", properties.Select(Column)) + ";";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new(CommandStatus.AlreadyExists);
        }

        for (var index = 0; index < properties.Length; index++)
        {
            object? value = null;
            if (!await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false))
            {
                var mapping = properties[index].GetRelationalTypeMapping();
                value = mapping.GetDataReaderMethod().Invoke(reader, [index]);
                if (mapping.Converter is not null)
                {
                    value = mapping.Converter.ConvertFromProvider(value);
                }
            }

            properties[index].PropertyInfo!.SetValue(entity, value);
        }

        return new(CommandStatus.Inserted, 1, entity, hasValue: true);
    }
}