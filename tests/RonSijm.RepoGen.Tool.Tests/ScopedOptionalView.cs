namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScopedOptionalView(int PartitionId, int ZoneId, int Id, int Version, List<ScopedOptionalData> Links);
