namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only selection/fresh-result filter, never a Save constraint or insert predicate.</summary>
internal sealed record InMemoryCommandModelQueryFilterModel(InMemoryModelQueryFilterModel Root, string PredicateBodySource);
