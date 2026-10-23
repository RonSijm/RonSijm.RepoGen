using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sqlite;

/// <summary>SQLite INSERT ON CONFLICT DO NOTHING RETURNING, protected by a declared non-null, unfiltered unique key.</summary>
[RepositoryProvider("Microsoft.EntityFrameworkCore.Sqlite")]
public sealed class SqliteAtomicWriter : RelationalAtomicWriter
{
    protected override string ProviderName => "Microsoft.EntityFrameworkCore.Sqlite";

    protected override void ValidateProviderModel(IEntityType metadata, bool includeShadowProperties, bool includeMappedProperties)
    {
        if (metadata.GetTableName() is null || metadata.GetSchema() is not null || metadata.BaseType is not null || metadata.GetDerivedTypes().Any() ||
            metadata.IsOwned() || metadata.HasSharedClrType || metadata.GetTableMappings().Count() != 1)
            throw new NotSupportedException("Atomic SQLite writes require a single ordinary non-inherited table mapping without a schema.");
        if (!includeShadowProperties && metadata.GetProperties().Any(static property => property.PropertyInfo?.SetMethod is null))
            throw new NotSupportedException("Atomic returned values require mapped CLR scalar properties with setters; shadow/backing-field-only mappings are unsupported.");
    }

    protected override void ValidateInsertProperties(IReadOnlyList<IProperty> properties, StoreObjectIdentifier store)
    {
        if (properties.Any(static property => property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save || property.ValueGenerated == ValueGenerated.OnAddOrUpdate))
            throw new InvalidOperationException("Atomic insert cannot assign ignored/computed/store-generated-on-update columns.");
    }

    protected override string CommandText(ISqlGenerationHelper helper, IEntityType metadata, IReadOnlyList<IProperty> insert,
        IReadOnlyList<IProperty> conflicts, IReadOnlyList<IProperty> returned)
    {
        var store = StoreObjectIdentifier.Table(metadata.GetTableName()!, null);
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        return "INSERT INTO " + helper.DelimitIdentifier(metadata.GetTableName()!) + " (" + string.Join(", ", insert.Select(Column)) + ") VALUES (" +
            string.Join(", ", Enumerable.Range(0, insert.Count).Select(static index => "@p" + index)) + ") ON CONFLICT (" +
            string.Join(", ", conflicts.Select(Column)) + ") DO NOTHING RETURNING " + string.Join(", ", returned.Select(Column)) + ";";
    }
}
