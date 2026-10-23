namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedKeyChildSeed(int Id, int? OwnerScope, string? OwnerCode, string Name, bool Deleted = false);
