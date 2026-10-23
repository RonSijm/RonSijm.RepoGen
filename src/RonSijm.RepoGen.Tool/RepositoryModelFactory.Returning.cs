using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Internal;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static void ValidateReturningMapping(IEntityType entity, LambdaExpression projection, string? providerName, bool includeShadowProperties, bool includeMappedProperties)
    {
        var table = entity.GetTableName();
        var schema = entity.GetSchema();
        if (table is null || entity.GetViewName() is not null || entity.GetSqlQuery() is not null || entity.FindPrimaryKey() is null || entity.BaseType is not null || entity.GetDerivedTypes().Any() ||
            entity.IsOwned() || entity.HasSharedClrType || entity.GetTableMappings().Count() != 1 || entity.GetDeclaredTriggers().Any() || entity.ClrType.IsAbstract ||
            entity.ClrType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) is null ||
            entity.Model.GetEntityTypes().Any(other => other != entity && other.GetTableName() == table && other.GetSchema() == schema))
            throw new InvalidOperationException("RGD1306: Returning writes require one ordinary, unshared, non-inherited table with a key, a parameterless entity constructor, and no declared triggers.");

        var store = StoreObjectIdentifier.Table(table, schema);
        var properties = entity.GetProperties().ToArray();
        if (properties.Any(property => property.GetColumnName(store) is null || !includeMappedProperties &&
                !((includeShadowProperties && property.IsShadowProperty()) || ReturningPropertyAccess.Ordinary(property))) ||
            entity.FindPrimaryKey()!.Properties.Any(static property => !ReturningPropertyAccess.Ordinary(property)) ||
            properties.Select(property => property.GetColumnName(store)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length ||
            entity.FindAnnotation("SqlServer:MemoryOptimized")?.Value is true)
            throw new InvalidOperationException(includeMappedProperties
                ? "RGD1306: Mapped returning writes require distinct scalar columns and ordinary CLR keys/getters/setters; field/indexer/shadow keys and memory-optimized mappings remain unsupported."
                : "RGD1306: Returning writes require distinct mapped scalar columns and CLR keys/getters/setters. Shadow values require IncludeShadowProperties; backing-field/indexer and memory-optimized mappings remain unsupported.");

        if (includeMappedProperties)
        {
            try
            {
                foreach (var property in properties.Where(static property => !property.IsShadowProperty()))
                    _ = ReturningPropertyAccess.MaterializationMember(property);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                throw new InvalidOperationException("RGD1306: IncludeMappedProperties requires materializable scalar fields/properties/indexers. " + exception.Message, exception);
            }
        }

        if (providerName == "Microsoft.EntityFrameworkCore.Sqlite" && schema is not null)
            throw new InvalidOperationException("RGD1306: SQLite returning writes do not support schemas.");

        new ReturningProjectionValidator(entity, projection.Parameters[0], includeShadowProperties, includeMappedProperties).Visit(projection.Body);
    }
}
