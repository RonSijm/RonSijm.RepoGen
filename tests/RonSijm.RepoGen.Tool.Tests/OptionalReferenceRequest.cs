namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OptionalReferenceRequest(int OwnerId, IReadOnlyList<OptionalReferenceItem> Items, int ExpectedVersion = 1);

