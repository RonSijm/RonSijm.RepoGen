namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryBatchRequest(IReadOnlyList<MemoryWriteRequest> Items, string ContextKey);
