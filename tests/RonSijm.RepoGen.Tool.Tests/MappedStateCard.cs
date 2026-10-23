namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedStateCard(int Id, string Title, int Counter, string Caption, int Audit,
    int Revision, int? OptionalVersion, byte[]? Stamp);
