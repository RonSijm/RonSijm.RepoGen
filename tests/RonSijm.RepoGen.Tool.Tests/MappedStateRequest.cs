namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedStateRequest(int Id, string Title, int Version = 3, int? OptionalVersion = 1, byte[]? Stamp = null);
