namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedCollectionChildSeed(int Id, int ParentId, string Code, string Text, int Counter,
    string Category, int Audit, int Revision, int? OptionalVersion, byte[]? Stamp, string Extra = "seed");
