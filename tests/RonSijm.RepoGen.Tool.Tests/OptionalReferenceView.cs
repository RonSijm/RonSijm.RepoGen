namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OptionalReferenceView(int Id, int Version, IReadOnlyList<OptionalReferenceData> Links);

