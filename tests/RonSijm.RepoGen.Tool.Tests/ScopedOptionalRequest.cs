namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScopedOptionalRequest(int PartitionId, int ZoneId, int OwnerId,
    IReadOnlyList<ScopedOptionalItem>? Items, int ExpectedVersion = 1, bool Apply = true);
