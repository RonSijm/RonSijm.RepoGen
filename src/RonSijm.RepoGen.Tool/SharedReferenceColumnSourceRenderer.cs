using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class SharedReferenceColumnSourceRenderer
{
    internal static string Predicate(ConfiguredLinkReconciliationModel link, bool fixture)
        => "(__repoGenReferenceOwner, __repoGenReferenceKey) => " + string.Join(" && ", link.SharedReferenceColumns!.Select(column =>
            "global::System.Collections.Generic.EqualityComparer<" + column.TypeName + ">.Default.Equals(" +
            (fixture ? "__repoGenReferenceOwner." + RepositoryModelFactory.EscapeIdentifier(column.OwnerPropertyName) :
                "context.Entry(__repoGenReferenceOwner).Property<" + column.TypeName + ">(" + JsonSerializer.Serialize(column.OwnerPropertyName) + ").CurrentValue") +
            ", " + (link.TargetForeignKeys.Count == 1 ? "__repoGenReferenceKey" : "__repoGenReferenceKey.Item" + (column.ReferenceKeyIndex + 1)) + ")"));
}
