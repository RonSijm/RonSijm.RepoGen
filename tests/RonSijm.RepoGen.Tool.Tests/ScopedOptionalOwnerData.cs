namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScopedOptionalOwnerData(int PartitionId, int ZoneId, int Id, string Name, int Version = 1);
