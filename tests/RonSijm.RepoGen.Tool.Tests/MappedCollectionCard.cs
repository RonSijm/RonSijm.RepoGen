namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedCollectionCard(int Id, string Code, string Text, string Category, int Audit,
    int Revision, int? OptionalVersion, byte[]? Stamp);
