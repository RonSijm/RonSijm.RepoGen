namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedAtomicRequest(int Id, string Title, int Delta = 1, int Revision = 3,
    int? OptionalVersion = 1, byte[]? Stamp = null, int Minimum = 0);
