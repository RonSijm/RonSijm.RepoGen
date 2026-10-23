namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryWriteRequest(int Id, string Title, int ExpectedVersion = 1, bool Supplied = true, string ContextKey = "A");
