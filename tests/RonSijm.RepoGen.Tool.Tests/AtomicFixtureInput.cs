namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AtomicFixtureInput(int OwnerId, string Code = "new", int Amount = 2, int ExpectedVersion = 1,
    byte[]? Stamp = null, int Id = 0, bool Hidden = false, int Minimum = 0, string Partition = "A");
