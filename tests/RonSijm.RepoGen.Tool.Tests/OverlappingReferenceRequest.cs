namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OverlappingReferenceRequest(int PartitionId, int ZoneId, int OwnerId,
    IReadOnlyList<OverlappingReferenceItem>? Items, int ExpectedVersion = 1, bool Apply = true);
