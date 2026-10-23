namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryCollectionRequest(int Id, int OwnerId, string Name, IReadOnlyList<MemoryCollectionItem>? Items, int ExpectedVersion = 1);
