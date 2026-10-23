namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedStateSeed(int Id, string Title, int Counter, string Caption, int Audit,
    int Revision, int? OptionalVersion, byte[]? Stamp, string Extra = "arranged");
