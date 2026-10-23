using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class SharedReferenceColumnSourceRenderer
{
    internal static bool HasCreatedOwnerGuard(ConfiguredLinkReconciliationModel? link)
        => link?.AdditionalReferences?.Any(static reference => reference.SharedColumns?.Any(static column => column.OwnerPropertyName is not null) == true) == true;

    internal static string CreatedOwnerPredicate(ConfiguredLinkReconciliationModel link, bool fixture)
        => "(__repoGenReferenceOwner, __repoGenNewLink) => " + string.Join(" && ", link.AdditionalReferences!
            .SelectMany(static reference => reference.SharedColumns ?? []).Where(static column => column.OwnerPropertyName is not null)
            .DistinctBy(static column => column.PropertyName).OrderBy(static column => column.PropertyName, StringComparer.Ordinal)
            .Select(column => "global::System.Collections.Generic.EqualityComparer<" + column.TypeName + ">.Default.Equals(" +
                (fixture ? "__repoGenReferenceOwner." + RepositoryModelFactory.EscapeIdentifier(column.OwnerPropertyName!) :
                    "context.Entry(__repoGenReferenceOwner).Property<" + column.TypeName + ">(" + JsonSerializer.Serialize(column.OwnerPropertyName) + ").CurrentValue") +
                ", __repoGenNewLink." + RepositoryModelFactory.EscapeIdentifier(column.PropertyName) + ")"));

    internal static string Predicate(ConfiguredLinkReconciliationModel link, bool fixture)
        => "(__repoGenReferenceOwner, __repoGenReferenceKey) => " + string.Join(" && ", link.SharedReferenceColumns!.Select(column =>
            "global::System.Collections.Generic.EqualityComparer<" + column.TypeName + ">.Default.Equals(" +
            (fixture ? "__repoGenReferenceOwner." + RepositoryModelFactory.EscapeIdentifier(column.OwnerPropertyName) :
                "context.Entry(__repoGenReferenceOwner).Property<" + column.TypeName + ">(" + JsonSerializer.Serialize(column.OwnerPropertyName) + ").CurrentValue") +
            ", " + (link.TargetForeignKeys.Count == 1 ? "__repoGenReferenceKey" : "__repoGenReferenceKey.Item" + (column.ReferenceKeyIndex + 1)) + ")"));
}
