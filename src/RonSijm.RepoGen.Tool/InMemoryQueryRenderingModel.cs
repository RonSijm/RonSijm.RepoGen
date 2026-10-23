namespace RonSijm.RepoGen.Tool;

/// <summary>Independent fixture expression view; never serialized into production/cache/API definitions.</summary>
internal sealed record InMemoryQueryRenderingModel(ConfiguredQueryModel Query, IReadOnlyList<SortModel> DefaultSorts,
    IReadOnlyList<VisibilityPolicyModel> VisibilityPolicies);
