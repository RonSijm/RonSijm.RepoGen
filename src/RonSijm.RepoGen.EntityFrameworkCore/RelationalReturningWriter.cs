using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Internal;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Infrastructure base for native returning providers. EF translates and parameterizes the key/value source.</summary>
/// <remarks>Raw native DML does not run through EF's ExecuteUpdate/SaveChanges interceptor pipeline.</remarks>
public abstract class RelationalReturningWriter : IRepositoryReturningMappedStateWriter
{
    protected abstract string ProviderName { get; }
    protected virtual void ValidateProviderModel(IEntityType entityType) { }

    protected abstract string CommandText(ISqlGenerationHelper helper, IEntityType entityType, string sourceSql,
        ReturningWriteKind kind, IReadOnlyList<IProperty> keys, IReadOnlyList<IProperty> updates, IReadOnlyList<ReturningWriteAssignment> assignments, IReadOnlyList<IProperty> returned);

    public Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<TEntity, TEntity>(context, source, kind, assignments, includeShadowProperties: false, includeMappedProperties: false,
            static (entity, _) => entity, cancellationToken);

    public Task<IReadOnlyList<ReturningWriteRow<TEntity>>> ExecuteWithStateAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<TEntity, ReturningWriteRow<TEntity>>(context, source, kind, assignments, includeShadowProperties: true, includeMappedProperties: false,
            static (entity, values) => new(entity, values!), cancellationToken);

    public Task<IReadOnlyList<ReturningWriteRow<TEntity>>> ExecuteWithMappedStateAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<TEntity, ReturningWriteRow<TEntity>>(context, source, kind, assignments, includeShadowProperties: true, includeMappedProperties: true,
            static (entity, values) => new(entity, values!), cancellationToken);

    private async Task<IReadOnlyList<TRow>> ExecuteCoreAsync<TEntity, TRow>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, bool includeShadowProperties, bool includeMappedProperties,
        Func<TEntity, IReadOnlyDictionary<string, object?>?, TRow> materialize, CancellationToken cancellationToken) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(assignments);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.ProviderName != ProviderName)
        {
            throw new NotSupportedException(GetType().Name + " requires '" + ProviderName + "'.");
        }

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Returning writes require an operation transaction.");
        }

        var metadata = context.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException("The returning entity is not mapped.");
        ValidateModel(metadata, includeShadowProperties, includeMappedProperties);
        ValidateProviderModel(metadata);
        var keys = metadata.FindPrimaryKey()!.Properties;
        var properties = metadata.GetProperties().ToArray();
        var mappedAssignments = includeMappedProperties ? ReturningMappedMaterializer.CreatePlan(properties) : null;
        var updates = assignments.Select(assignment => metadata.FindProperty(assignment.PropertyName) ?? throw new ArgumentException("Unknown update property.", nameof(assignments))).ToArray();
        if (kind is not ReturningWriteKind.Update and not ReturningWriteKind.Delete ||
            kind == ReturningWriteKind.Update && updates.Length == 0 || kind == ReturningWriteKind.Delete && updates.Length != 0 ||
            updates.Distinct().Count() != updates.Length)
        {
            throw new ArgumentException("Choose Update with distinct scalar assignments, or Delete without assignments.", nameof(assignments));
        }

        if (updates.Any(static property => !ReturningPropertyAccess.Ordinary(property) || property.IsKey() || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save ||
                                           property.ValueGenerated != ValueGenerated.Never))
        {
            throw new InvalidOperationException("Returning updates cannot assign immutable keys or generated/read-only columns.");
        }

        ValidateSource(source, keys, updates, assignments);
        source = RequireServerValues(source, assignments);
        // CreateDbCommand keeps EF's typed DbParameters (including value converters), not ToQueryString debug literals.
        await using var command = source.CreateDbCommand();
        if (!ReferenceEquals(command.Connection, context.Database.GetDbConnection()))
        {
            throw new InvalidOperationException("The returning source must use the operation's DbContext connection.");
        }

        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (context.Database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        command.CommandText = CommandText(context.GetService<ISqlGenerationHelper>(), metadata, command.CommandText.TrimEnd().TrimEnd(';'),
            kind, keys, updates, assignments, properties);
        var rows = new List<TRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = (TEntity)Activator.CreateInstance(typeof(TEntity), nonPublic: true)!;
                var values = includeShadowProperties ? new Dictionary<string, object?>(StringComparer.Ordinal) : null;
                for (var index = 0; index < properties.Length; index++)
                {
                    var value = await ReadValueAsync(reader, properties[index], index, cancellationToken).ConfigureAwait(false);
                    if (mappedAssignments is not null)
                    {
                        mappedAssignments[index]?.Invoke(row, value);
                    }
                    else if (!properties[index].IsShadowProperty())
                    {
                        properties[index].PropertyInfo!.SetValue(row, value);
                    }
                    values?.Add(properties[index].Name, value);
                }

                rows.Add(materialize(row, values));
            }

            // OUTPUT can deliver rows before a later statement failure. Never report success until the command finishes.
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
            }
        }

        return rows.AsReadOnly();
    }

    /// <summary>Checks the supported ordinary scalar table mapping without opening a connection.</summary>
    public static void ValidateModel(IEntityType metadata, bool includeShadowProperties = false, bool includeMappedProperties = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var table = metadata.GetTableName();
        var schema = metadata.GetSchema();
        if (table is null || metadata.GetViewName() is not null || metadata.GetSqlQuery() is not null || metadata.FindPrimaryKey() is null || metadata.BaseType is not null ||
            metadata.GetDerivedTypes().Any() || metadata.IsOwned() || metadata.HasSharedClrType || metadata.GetTableMappings().Count() != 1 ||
            metadata.GetDeclaredTriggers().Any() || metadata.ClrType.IsAbstract ||
            metadata.ClrType.GetConstructor(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                binder: null, Type.EmptyTypes, modifiers: null) is null ||
            metadata.Model.GetEntityTypes().Any(other => other != metadata && other.GetTableName() == table && other.GetSchema() == schema))
        {
            throw new NotSupportedException("Returning writes require one ordinary, unshared, non-inherited table with a parameterless entity constructor and no declared triggers.");
        }

        var store = StoreObjectIdentifier.Table(table, schema);
        var properties = metadata.GetProperties().ToArray();
        if (properties.Any(property => property.GetColumnName(store) is null || !includeMappedProperties &&
                !((includeShadowProperties && property.IsShadowProperty()) || ReturningPropertyAccess.Ordinary(property))) ||
            metadata.FindPrimaryKey()!.Properties.Any(static property => !ReturningPropertyAccess.Ordinary(property)) ||
            properties.Select(property => property.GetColumnName(store)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
        {
            throw new NotSupportedException(includeMappedProperties
                ? "Returned mapped rows require distinct scalar columns and ordinary CLR keys/getters/setters; field/indexer/shadow keys remain unsupported."
                : "Returned rows require distinct mapped scalar columns and CLR keys/getters/setters; shadow values require explicit state capture, and backing-field/indexer mappings remain unsupported.");
        }
        if (includeMappedProperties)
            foreach (var property in properties.Where(static property => !property.IsShadowProperty()))
                _ = ReturningPropertyAccess.MaterializationMember(property);
    }

    public static string KeyAlias(int index) => "__repoGenKey" + index;
    public static string ValueAlias(int index) => "__repoGenValue" + index;

    private static void ValidateSource(IQueryable source, IReadOnlyList<IProperty> keys, IProperty[] updates, IReadOnlyList<ReturningWriteAssignment> assignments)
    {
        var fields = source.ElementType.GetProperties();
        var expected = keys.Select((key, index) => (Name: KeyAlias(index), key.ClrType)).ToDictionary(static field => field.Name, static field => field.ClrType, StringComparer.Ordinal);
        var allowed = expected.Keys.Concat(Enumerable.Range(0, updates.Length).Select(ValueAlias)).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < updates.Length; index++)
        {
            var name = assignments[index].ValueAlias;
            var type = SourceValueType(updates[index]);
            if (!allowed.Contains(name) || expected.TryGetValue(name, out var prior) && prior != type)
            {
                throw new ArgumentException("An assignment must use a generated alias with the mapped provider CLR type.", nameof(assignments));
            }

            expected[name] = type;
        }

        if (fields.Length != expected.Count || expected.Any(field => fields.SingleOrDefault(property => property.Name == field.Key)?.PropertyType != field.Value))
        {
            throw new ArgumentException("The returning source must be the generated, ordered key/value projection.", nameof(source));
        }
    }

    private static Type SourceValueType(IProperty property)
    {
        var type = property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
        return (Nullable.GetUnderlyingType(property.ClrType) is not null || !property.ClrType.IsValueType) && type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? typeof(Nullable<>).MakeGenericType(type)
            : type;
    }

    private static IQueryable RequireServerValues(IQueryable source, IReadOnlyList<ReturningWriteAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            return source;
        }

        var row = Expression.Parameter(source.ElementType, "row");
        Expression? condition = null;
        foreach (var assignment in assignments.DistinctBy(static assignment => assignment.ValueAlias))
        {
            var value = Expression.Property(row, assignment.ValueAlias);
            // A query predicate must translate in full. Referencing each setter here prevents EF's
            // top-level client-projection escape hatch from silently writing its untransformed inputs.
            Expression translated = Expression.Equal(value, value);
            if (!value.Type.IsValueType || Nullable.GetUnderlyingType(value.Type) is not null)
            {
                translated = Expression.OrElse(translated, Expression.Equal(value, Expression.Constant(null, value.Type)));
            }

            condition = condition is null ? translated : Expression.AndAlso(condition, translated);
        }

        return source.Provider.CreateQuery(Expression.Call(typeof(Queryable), nameof(Queryable.Where), [source.ElementType], source.Expression,
            Expression.Quote(Expression.Lambda(condition!, row))));
    }

    private static async Task<object?> ReadValueAsync(DbDataReader reader, IProperty property, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            if (!property.IsNullable)
            {
                throw new InvalidOperationException("A non-nullable returned column was null: " + property.Name);
            }

            return null;
        }

        var mapping = property.GetRelationalTypeMapping();
        var value = mapping.GetDataReaderMethod().Invoke(reader, [ordinal]);
        return mapping.Converter is null ? value : mapping.Converter.ConvertFromProvider(value);
    }
}
