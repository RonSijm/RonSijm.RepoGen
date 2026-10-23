using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryCollectionOptionalRequest(int Id, Optional<IReadOnlyList<MemoryCollectionItem>?> Items);
