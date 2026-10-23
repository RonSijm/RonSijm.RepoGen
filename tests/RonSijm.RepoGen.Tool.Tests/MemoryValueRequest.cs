namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryValueRequest(int Id, string Title, int ExpectedLength = 3, string? Label = null);
