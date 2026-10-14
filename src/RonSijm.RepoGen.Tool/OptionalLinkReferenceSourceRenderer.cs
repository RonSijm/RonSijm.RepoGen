using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class OptionalLinkReferenceSourceRenderer
{
    internal static bool RequiresPreparation(string? creation, ConfiguredLinkReconciliationModel? link)
        => creation is not null && link?.AdditionalReferences?.Any(static reference => reference.Optional == true && reference.ForeignKeys.Count > 1) == true;

    internal static string CreateLambda(string? creation, ConfiguredLinkReconciliationModel? link)
    {
        if (creation is null) return "null";
        var optional = link?.AdditionalReferences?.Where(static reference => reference.Optional == true && reference.ForeignKeys.Count > 1).ToArray() ?? [];
        if (optional.Length == 0) return "item => " + creation;
        // Evaluate the approved initializer once and only for newly added rows. Add-only input
        // values on matched rows are intentionally neither validated nor applied.
        var guards = optional.Select(reference => "if (" + string.Join(" || ", reference.ForeignKeys.Skip(1).Select(property =>
            "(__repoGenNewLink." + RepositoryModelFactory.EscapeIdentifier(reference.ForeignKeys[0]) + " is null) != " +
            "(__repoGenNewLink." + RepositoryModelFactory.EscapeIdentifier(property) + " is null)")) +
            ") throw new global::System.ArgumentException(" + JsonSerializer.Serialize("Optional link reference '" + reference.NavigationName + "' must be entirely null or entirely present.") + ", \"items\");");
        return "item => { var __repoGenNewLink = " + creation + "; " + string.Join(" ", guards) + " return __repoGenNewLink; }";
    }
}
