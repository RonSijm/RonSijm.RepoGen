namespace RonSijm.RepoGen.Tool.Tests;

public sealed record CompositeCollectionRequest(int TenantId, int Id, IReadOnlyList<CompositeCollectionItem>? Items, int ExpectedVersion = 1);
