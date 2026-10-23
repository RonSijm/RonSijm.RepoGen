namespace RonSijm.RepoGen.Tool;

/// <summary>Independent fixture command expression view, excluded from production/API definitions.</summary>
internal sealed record InMemoryCommandRenderingModel(ConfiguredCommandModel Command, IReadOnlyList<VisibilityPolicyModel> VisibilityPolicies);
