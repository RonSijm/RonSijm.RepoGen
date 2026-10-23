namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedCollectionItem(int Id, string Code, string Text, int Revision = 3,
    int? OptionalVersion = 1, byte[]? Stamp = null, bool Supplied = true);
