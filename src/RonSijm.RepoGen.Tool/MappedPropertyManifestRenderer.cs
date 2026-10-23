using System.Text.Json;
using System.Text.Json.Nodes;

namespace RonSijm.RepoGen.Tool;

internal static class MappedPropertyManifestRenderer
{
    internal static bool HasBindings(EntityRepositoryModel entity) => entity.Lookups.Any(lookup => lookup.Properties.Any(property => property.MappedProperty is not null)) || Tokens(entity).Any();

    internal static void Append(JsonNode receipt, EntityRepositoryModel entity)
    {
        if (!HasBindings(entity)) return;
        receipt["mappedPropertyBindings"] = JsonSerializer.SerializeToNode(new
        {
            scope = "DesignTimeMetadataOnlyNoRuntimePropertyNames",
            accessors = entity.Lookups.SelectMany(lookup => lookup.Properties.Where(property => property.MappedProperty is not null)
                .Select(property => new { method = lookup.MethodName, name = property.PropertyName, modelType = property.MappedProperty!.TypeName, shadow = property.MappedProperty.IsShadow, unique = lookup.IsUnique })),
            expectedVersions = Tokens(entity).Select(token => new { operation = token.Operation, scope = token.Scope, name = token.Binding.Name, modelType = token.Binding.TypeName, shadow = token.Binding.IsShadow }),
            assignments = "NoneTokenAdvancementMustBeConfiguredSeparately",
            fixtures = entity.InMemoryStore?.WriteMapping?.MappedPropertyState is { Count: > 0 }
                ? "SeePerOperationMappedScalarStateCoverageOrTypedOverride"
                : "ShadowAndNonPublicTokenStateRequiresTypedOverride"
        });
    }

    private static IEnumerable<(string Operation, string Scope, MappedPropertyBindingModel Binding)> Tokens(EntityRepositoryModel entity)
    {
        foreach (var command in entity.Commands.OrderBy(command => command.MethodName, StringComparer.Ordinal))
        {
            foreach (var version in command.ExpectedVersions ?? [])
                if (version.MappedProperty is { } binding) yield return (command.MethodName, "Entity", binding);
            foreach (var version in command.Batch?.ExpectedVersions ?? [])
                if (version.MappedProperty is { } binding) yield return (command.MethodName, "BatchItem", binding);
            foreach (var collection in command.Collections ?? [])
                foreach (var version in collection.ExpectedVersions)
                    if (version.MappedProperty is { } binding) yield return (command.MethodName, "Collection:" + collection.NavigationName, binding);
        }
    }
}
