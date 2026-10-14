namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only row filter; never part of the production query or cache model.</summary>
internal sealed record InMemoryModelQueryFilterModel(string EntityTypeName, string PredicateSource);
