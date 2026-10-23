namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedPropertyInput(int Id, int Version = 1, string Title = "changed", int? OptionalVersion = null,
    byte[]? BinaryVersion = null, ReturningState State = ReturningState.Pending);
