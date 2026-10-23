namespace RonSijm.RepoGen.Tool.Tests;

public sealed record OverlappingReferenceView(int PartitionId, int ZoneId, int Id, int Version, List<string> References);
