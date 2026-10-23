using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class OptionalLinkReferenceSourceRenderer
{
    internal static bool RequiresPreparation(string? creation, ConfiguredLinkReconciliationModel? link)
        => creation is not null && link?.AdditionalReferences?.Any(static reference => reference.Optional == true && Presence(reference).Count > 1) == true;

    internal static string CreateLambda(string? creation, ConfiguredLinkReconciliationModel? link)
    {
        if (creation is null) return "null";
        var optional = link?.AdditionalReferences?.Where(static reference => reference.Optional == true && Presence(reference).Count > 1).ToArray() ?? [];
        if (optional.Length == 0) return "item => " + creation;
        // Evaluate the approved initializer once and only for newly added rows. Add-only input
        // values on matched rows are intentionally neither validated nor applied.
        var guards = optional.Select(reference => "if (" + string.Join(" || ", Presence(reference).Skip(1).Select(property =>
            "(__repoGenNewLink." + RepositoryModelFactory.EscapeIdentifier(Presence(reference)[0]) + " is null) != " +
            "(__repoGenNewLink." + RepositoryModelFactory.EscapeIdentifier(property) + " is null)")) +
            ") throw new global::System.ArgumentException(" + JsonSerializer.Serialize("Optional link reference '" + reference.NavigationName + "'" +
                (reference.OptionalPresenceColumns is null ? " must be entirely null or entirely present." : " must have its independent components entirely null or entirely present.")) + ", \"items\");");
        return "item => { var __repoGenNewLink = " + creation + "; " + string.Join(" ", guards) + " return __repoGenNewLink; }";
    }

    private static IReadOnlyList<string> Presence(ConfiguredAdditionalReferenceModel reference)
        => reference.OptionalPresenceColumns ?? reference.ForeignKeys;
}
