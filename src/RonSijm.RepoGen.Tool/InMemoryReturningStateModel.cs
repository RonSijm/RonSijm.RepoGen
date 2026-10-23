namespace RonSijm.RepoGen.Tool;

/// <summary>Explicit scalar fixture state used by native returned-value projections, not provider buffers.</summary>
internal sealed record InMemoryReturningStateModel(bool IncludeShadowProperties, bool IncludeMappedProperties);
