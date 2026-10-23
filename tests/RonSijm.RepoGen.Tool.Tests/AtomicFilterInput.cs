namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AtomicFilterInput(int Id, string Title = "new", int ExpectedVersion = 0,
    bool Deleted = false, bool Hidden = false, string Partition = "A");
