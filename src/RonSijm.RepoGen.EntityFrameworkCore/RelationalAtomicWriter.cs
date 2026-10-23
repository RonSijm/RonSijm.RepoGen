using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Internal;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Shared parameterized insert/conflict and detached scalar capture pipeline for native atomic providers.</summary>
public abstract class RelationalAtomicWriter : IRepositoryAtomicMappedStateWriter
{
    protected abstract string ProviderName { get; }
    protected abstract void ValidateProviderModel(IEntityType metadata, bool includeShadowProperties, bool includeMappedProperties);
    protected abstract void ValidateInsertProperties(IReadOnlyList<IProperty> properties, StoreObjectIdentifier store);
    protected abstract string CommandText(ISqlGenerationHelper helper, IEntityType metadata, IReadOnlyList<IProperty> insert,
        IReadOnlyList<IProperty> conflicts, IReadOnlyList<IProperty> returned);

    public Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, entity, columns, conflictColumns, false, false, static (row, _) => row, cancellationToken);

    public Task<CommandResult<ReturningWriteRow<TEntity>>> InsertIfAbsentWithStateAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, entity, columns, conflictColumns, true, false, static (row, values) => new ReturningWriteRow<TEntity>(row, values!), cancellationToken);

    public Task<CommandResult<ReturningWriteRow<TEntity>>> InsertIfAbsentWithMappedStateAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, entity, columns, conflictColumns, true, true, static (row, values) => new ReturningWriteRow<TEntity>(row, values!), cancellationToken);

    private async Task<CommandResult<TRow>> ExecuteAsync<TEntity, TRow>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, bool includeShadowProperties, bool includeMappedProperties,
        Func<TEntity, IReadOnlyDictionary<string, object?>?, TRow> materialize, CancellationToken cancellationToken) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(conflictColumns);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.ProviderName != ProviderName)
            throw new NotSupportedException(GetType().Name + " requires '" + ProviderName + "'.");
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Atomic insert requires the operation transaction.");

        var metadata = context.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException("The atomic entity is not mapped.");
        ValidateProviderModel(metadata, includeShadowProperties, includeMappedProperties);
        if (includeShadowProperties) ValidateCapturedModel(metadata, includeMappedProperties);
        var properties = metadata.GetProperties().ToArray();
        var mappedAssignments = includeMappedProperties ? ReturningMappedMaterializer.CreatePlan(properties) : null;
        var insert = columns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown insert property.", nameof(columns))).ToArray();
        var conflicts = conflictColumns.Select(name => metadata.FindProperty(name) ?? throw new ArgumentException("Unknown conflict property.", nameof(conflictColumns))).ToArray();
        if (insert.Any(static property => !ReturningPropertyAccess.Ordinary(property)))
            throw new NotSupportedException("Atomic insert assignments require ordinary CLR scalar getters/setters; capture does not permit field/indexer/shadow assignments.");
        if (insert.Length == 0 || insert.Distinct().Count() != insert.Length || conflicts.Length == 0 || conflicts.Distinct().Count() != conflicts.Length ||
            conflicts.Any(property => property.IsNullable || !insert.Contains(property) || property.PropertyInfo!.GetValue(entity) is null) ||
            !metadata.GetKeys().Any(key => key.Properties.SequenceEqual(conflicts)) &&
            !metadata.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.SequenceEqual(conflicts)))
            throw new InvalidOperationException("Atomic conflict columns must be supplied non-null properties of a declared primary/unique key or unfiltered unique index.");

        var store = StoreObjectIdentifier.Table(metadata.GetTableName()!, metadata.GetSchema());
        ValidateInsertProperties(insert, store);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (context.Database.GetCommandTimeout() is { } timeout) command.CommandTimeout = timeout;
        for (var index = 0; index < insert.Length; index++)
            command.Parameters.Add(insert[index].GetRelationalTypeMapping().CreateParameter(command, "@p" + index,
                insert[index].PropertyInfo!.GetValue(entity), insert[index].IsNullable));
        command.CommandText = CommandText(context.GetService<ISqlGenerationHelper>(), metadata, insert, conflicts, properties);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var inserted = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var values = new object?[properties.Length];
        if (inserted)
        {
            for (var index = 0; index < properties.Length; index++)
                values[index] = await ReadValueAsync(reader, properties[index], index, cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Atomic insertion returned more than one row.");
        }
        // OUTPUT may return a row before a later statement error. Drain before materializing/reporting success.
        while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
        if (!inserted) return new(CommandStatus.AlreadyExists);

        var captured = includeShadowProperties ? new Dictionary<string, object?>(StringComparer.Ordinal) : null;
        for (var index = 0; index < properties.Length; index++)
        {
            if (mappedAssignments is not null) mappedAssignments[index]?.Invoke(entity, values[index]);
            else if (!properties[index].IsShadowProperty()) properties[index].PropertyInfo!.SetValue(entity, values[index]);
            captured?.Add(properties[index].Name, values[index]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(CommandStatus.Inserted, 1, materialize(entity, captured), hasValue: true);
    }

    private static async Task<object?> ReadValueAsync(DbDataReader reader, IProperty property, int index, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false)) return null;
        var mapping = property.GetRelationalTypeMapping();
        var value = mapping.GetDataReaderMethod().Invoke(reader, [index]);
        return mapping.Converter is null ? value : mapping.Converter.ConvertFromProvider(value);
    }

    /// <summary>Checks the explicitly captured scalar boundary without acquiring a connection.</summary>
    protected static void ValidateCapturedModel(IEntityType metadata, bool includeMappedProperties)
    {
        var table = metadata.GetTableName();
        var schema = metadata.GetSchema();
        if (table is null || metadata.GetViewName() is not null || metadata.GetSqlQuery() is not null || metadata.FindPrimaryKey() is null ||
            metadata.BaseType is not null || metadata.GetDerivedTypes().Any() || metadata.IsOwned() || metadata.HasSharedClrType ||
            metadata.GetTableMappings().Count() != 1 || metadata.GetDeclaredTriggers().Any() ||
            metadata.Model.GetEntityTypes().Any(other => other != metadata && other.GetTableName() == table && other.GetSchema() == schema))
            throw new NotSupportedException("Atomic scalar capture requires one ordinary unshared table with a key and no declared triggers.");
        var store = StoreObjectIdentifier.Table(table, schema);
        var properties = metadata.GetProperties().ToArray();
        if (metadata.FindPrimaryKey()!.Properties.Any(static property => !ReturningPropertyAccess.Ordinary(property)) ||
            properties.Any(property => property.GetColumnName(store) is null || !includeMappedProperties &&
                !property.IsShadowProperty() && !ReturningPropertyAccess.Ordinary(property)) ||
            properties.Select(property => property.GetColumnName(store)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
            throw new NotSupportedException("Atomic capture requires distinct scalar columns and ordinary CLR keys; field/indexer values need mapped-state permission.");
        if (includeMappedProperties)
            foreach (var property in properties.Where(static property => !property.IsShadowProperty()))
                _ = ReturningPropertyAccess.MaterializationMember(property);
    }
}
