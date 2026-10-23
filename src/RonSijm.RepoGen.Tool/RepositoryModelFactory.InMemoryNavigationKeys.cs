using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static bool ValidateInMemoryNavigationKeys(string name,
        IReadOnlyList<(IEntityType Entity, IReadOnlyList<IProperty> Properties)> groups,
        Dictionary<IEntityType, InMemoryEntityMappingModel?> mappings, IModel? relationshipMetadata, SortedSet<string> reasons,
        Dictionary<IProperty, InMemoryRelationshipComparerModel>? comparers = null, bool useMappedPropertyKeys = false)
    {
        bool Readable(IProperty property, IEntityType declaring) => property.ClrType.IsVisible &&
            InMemoryNavigationKeyMember(property, mappings[declaring], useMappedPropertyKeys) is not null;
        if (groups.Any(group => group.Properties.Any(property => !Readable(property, group.Entity))))
        {
            reasons.Add(useMappedPropertyKeys
                ? "Navigation '" + name + "' requires an ordinary scalar assignment or an exact MapInMemoryProperty binding for every principal/foreign-key component; missing mapped property keys require a script."
                : "Navigation '" + name + "' requires public readable CLR key properties or explicitly mapped scalar keys; missing mapped keys and shadow/backing-field-only relationship keys require a script.");
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

    private static string? InMemoryNavigationKeyMember(IProperty property, InMemoryEntityMappingModel? mapping, bool useMappedPropertyKeys)
    {
        if (mapping is null)
        {
            return property.PropertyInfo?.GetMethod?.IsPublic == true ? property.PropertyInfo.Name : null;
        }
        var ordinary = property.PropertyInfo?.Name ?? property.FieldInfo?.Name;
        if (ordinary is not null && mapping.Members.Any(member => member.Name == ordinary) &&
            mapping.PropertyMappings?.Any(binding => binding.MemberName == ordinary) != true)
        {
            return ordinary;
        }
        return useMappedPropertyKeys ? mapping.PropertyMappings?.SingleOrDefault(binding => binding.PropertyName == property.Name)?.MemberName : null;
    }

    private static string InMemoryNavigationKeysSource(IReadOnlyList<IProperty> properties, IEntityType entity,
        Dictionary<IEntityType, InMemoryEntityMappingModel?> mappings, bool useMappedPropertyKeys) => "static entity => new global::System.Object?[] { " +
        string.Join(", ", properties.Select(property => "entity." + EscapeIdentifier(
            InMemoryNavigationKeyMember(property, mappings[entity], useMappedPropertyKeys) ??
            throw new InvalidOperationException("Relationship key source must be validated before rendering.")))) + " }";

    private static List<InMemoryMappedRelationshipKeyModel>? InMemoryMappedRelationshipKeys(
        IReadOnlyList<(string Role, IEntityType Entity, IReadOnlyList<IProperty> Properties)> groups,
        Dictionary<IEntityType, InMemoryEntityMappingModel?> mappings, bool useMappedPropertyKeys)
    {
        if (!useMappedPropertyKeys)
        {
            return null;
        }
        var result = new List<InMemoryMappedRelationshipKeyModel>();
        foreach (var (role, entity, properties) in groups)
        {
            foreach (var property in properties)
            {
                if (mappings[entity]?.PropertyMappings?.SingleOrDefault(binding => binding.PropertyName == property.Name) is { } binding)
                {
                    result.Add(new(role, CSharpTypeName(entity.ClrType), property.Name, binding.MemberName, binding.TypeName, binding.AccessKind));
                }
            }
        }
        return result;
    }
}
