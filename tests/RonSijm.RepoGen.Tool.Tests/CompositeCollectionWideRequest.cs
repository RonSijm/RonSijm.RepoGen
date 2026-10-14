namespace RonSijm.RepoGen.Tool.Tests;

public sealed record CompositeCollectionWideRequest(int Id, IReadOnlyList<CompositeCollectionWideInput> Items);
