namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredAdditionalSharedColumnModel(string PropertyName, string TypeName,
    string? OwnerPropertyName, IReadOnlyList<string> SharedWith);
