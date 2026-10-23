using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.SqlServer;

/// <summary>SQL Server insert-if-absent with a serializable update-locked unique-key probe and OUTPUT INSERTED.</summary>
/// <remarks>The caller supplies an operation transaction. No MERGE, duplicate-error swallowing, or implicit retry is used.</remarks>
[RepositoryProvider("Microsoft.EntityFrameworkCore.SqlServer")]
public sealed class SqlServerAtomicWriter : RelationalAtomicWriter
{
    protected override string ProviderName => "Microsoft.EntityFrameworkCore.SqlServer";

    protected override void ValidateProviderModel(IEntityType metadata, bool includeShadowProperties, bool includeMappedProperties)
        => ValidateModel(metadata, includeShadowProperties, includeMappedProperties);

    protected override void ValidateInsertProperties(IReadOnlyList<IProperty> properties, StoreObjectIdentifier store)
    {
        if (properties.Any(property => property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save ||
            property.ValueGenerated == ValueGenerated.OnAddOrUpdate || property.GetValueGenerationStrategy(store) == SqlServerValueGenerationStrategy.IdentityColumn))
            throw new InvalidOperationException("Atomic insert cannot assign identity, ignored, computed or rowversion columns.");
    }

    protected override string CommandText(ISqlGenerationHelper helper, IEntityType metadata, IReadOnlyList<IProperty> insert,
        IReadOnlyList<IProperty> conflicts, IReadOnlyList<IProperty> returned)
    {
        var store = StoreObjectIdentifier.Table(metadata.GetTableName()!, metadata.GetSchema());
        var supplied = insert.ToArray();
        string Column(IProperty property) => helper.DelimitIdentifier(property.GetColumnName(store)!);
        var target = helper.DelimitIdentifier(metadata.GetTableName()!, metadata.GetSchema());
        return "INSERT INTO " + target + " (" + string.Join(", ", insert.Select(Column)) + ") OUTPUT " +
            string.Join(", ", returned.Select(property => "INSERTED." + Column(property))) + " SELECT " +
            string.Join(", ", Enumerable.Range(0, insert.Count).Select(static index => "@p" + index)) +
            " WHERE NOT EXISTS (SELECT 1 FROM " + target + " WITH (UPDLOCK, HOLDLOCK) WHERE " +
            string.Join(" AND ", conflicts.Select(property => Column(property) + " = @p" + Array.IndexOf(supplied, property))) + ");";
    }

    /// <summary>Validates the supported scalar table shape without acquiring a connection.</summary>
    public static void ValidateModel(IEntityType metadata, bool includeShadowProperties = false, bool includeMappedProperties = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var table = metadata.GetTableName();
        var schema = metadata.GetSchema();
        if (table is null || metadata.FindPrimaryKey() is null || metadata.BaseType is not null || metadata.GetDerivedTypes().Any() ||
            metadata.IsOwned() || metadata.HasSharedClrType || metadata.GetTableMappings().Count() != 1 ||
            metadata.GetDeclaredTriggers().Any() || metadata.IsMemoryOptimized() ||
            metadata.Model.GetEntityTypes().Any(other => other != metadata && other.GetTableName() == table && other.GetSchema() == schema))
            throw new NotSupportedException("Atomic SQL Server writes require one ordinary, unshared, non-inherited table without triggers or memory optimization.");
        if (includeShadowProperties || includeMappedProperties)
        {
            ValidateCapturedModel(metadata, includeMappedProperties);
            return;
        }
        var store = StoreObjectIdentifier.Table(table, schema);
        if (metadata.GetProperties().Any(property => property.PropertyInfo?.SetMethod is null || property.GetColumnName(store) is null))
            throw new NotSupportedException("Atomic returned values require mapped CLR scalar properties with setters; shadow/backing-field-only mappings are unsupported.");
    }
}
