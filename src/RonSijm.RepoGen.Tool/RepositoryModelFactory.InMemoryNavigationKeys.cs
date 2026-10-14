using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static bool ValidateInMemoryNavigationKeys(string name,
        IReadOnlyList<(IEntityType Entity, IReadOnlyList<IProperty> Properties)> groups,
        Dictionary<IEntityType, InMemoryEntityMappingModel?> mappings, IModel? relationshipMetadata, SortedSet<string> reasons,
        Dictionary<IProperty, InMemoryRelationshipComparerModel>? comparers = null)
    {
        bool Readable(IProperty property, IEntityType declaring) => property.ClrType.IsVisible &&
            (mappings[declaring] is { } mapped ? mapped.Members.Any(item => item.Name == (property.PropertyInfo?.Name ?? property.FieldInfo?.Name)) : property.PropertyInfo?.GetMethod?.IsPublic == true);
        if (groups.Any(group => group.Properties.Any(property => !Readable(property, group.Entity))))
        {
            reasons.Add("Navigation '" + name + "' requires public readable CLR key properties or explicitly mapped scalar keys; missing mapped keys and shadow/backing-field-only relationship keys require a script.");
            return false;
        }
        if (groups.SelectMany(static group => group.Properties).Any(property => InMemoryRelationshipMetadata.RequiresCustomEquality(property, relationshipMetadata) &&
            !(comparers?.ContainsKey(property) == true && InMemoryRelationshipMetadata.CanDeclareScalarEquality(property, relationshipMetadata))))
        {
            reasons.Add("Navigation '" + name + "' uses custom key conversion/comparison; ordinary structural CLR key equality does not simulate that provider relationship.");
            return false;
        }
        return true;
    }

    private static string InMemoryNavigationKeysSource(IReadOnlyList<IProperty> properties) => "static entity => new global::System.Object?[] { " +
        string.Join(", ", properties.Select(property => "entity." + EscapeIdentifier(property.PropertyInfo?.Name ?? property.FieldInfo!.Name))) + " }";
}
