namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedSetterSeed(int Id, string Title, string Caption, int Audit, int Version, string? Extra,
    string UniqueCode = "unique", int? OwnerId = null);
