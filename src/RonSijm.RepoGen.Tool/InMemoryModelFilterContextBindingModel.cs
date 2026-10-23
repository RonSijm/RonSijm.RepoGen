namespace RonSijm.RepoGen.Tool;

/// <summary>Auditable fixture-only replacement; not part of production/cache/API identity.</summary>
internal sealed record InMemoryModelFilterContextBindingModel(
    string ContextTypeName, string MemberName, string ContextKeyTypeName, string ValueTypeName, string ValueSource);
