using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryRelationshipComparerModel[] CreateInMemoryRelationshipComparers(IEntityType entity, RepositoryEntityDesign definition)
        => (definition.InMemoryStore?.RelationshipComparers ?? []).OrderBy(static value => value.PropertyName, StringComparer.Ordinal)
            .Select(declaration =>
            {
                InvalidOperationException Invalid(string reason) => new("RGD4501: Invalid fixture relationship comparer for '" + entity.ClrType.FullName + "." + declaration.PropertyName + "': " + reason);
                var property = entity.FindProperty(declaration.PropertyName);
                if (property is null || !property.IsKey() || property.IsNullable || Nullable.GetUnderlyingType(property.ClrType) is not null || !PortableWriteScalar(property.ClrType) || property.ClrType.IsArray ||
                    !property.ClrType.IsVisible || declaration.ValueType != property.ClrType)
                    throw Invalid("Select an ordinary, non-null mapped primary/alternate principal-key scalar with its exact CLR type; arrays, ordinary payload fields and FK-only fields are not principal identity declarations.");
                var factory = CreateInMemoryComparerFactory(entity.Model, property.ClrType, declaration.Comparer, Invalid);
                return new InMemoryRelationshipComparerModel(property.Name, CSharpTypeName(property.ClrType), factory);
            }).ToArray();

    private static string? InMemoryRelationshipComparison(IForeignKey foreignKey, RepositoryDesign? design,
        Dictionary<IProperty, InMemoryRelationshipComparerModel> effective)
    {
        var principal = foreignKey.PrincipalEntityType;
        var definition = design?.Entities.SingleOrDefault(entity => entity.EntityType == principal.ClrType);
        if (definition is null) return null;
        var declarations = CreateInMemoryRelationshipComparers(principal, definition);
        var components = foreignKey.PrincipalKey.Properties.Select((property, index) =>
        {
            var policy = declarations.SingleOrDefault(value => value.PropertyName == property.Name);
            if (policy is null) return "null";
            effective[property] = policy;
            effective[foreignKey.Properties[index]] = policy;
            return "global::RonSijm.RepoGen.Testing.InMemoryRelationshipKeyComparer.Component<" + policy.ValueTypeName + ">(" + policy.FactorySource + ")";
        }).ToArray();
        return components.All(static component => component == "null") ? null :
            "new global::RonSijm.RepoGen.Testing.InMemoryRelationshipKeyComparer(" + string.Join(", ", components) + ")";
    }
}
