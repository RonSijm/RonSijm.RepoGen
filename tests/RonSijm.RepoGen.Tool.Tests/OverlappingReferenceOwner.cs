namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceOwner
{
    public int PartitionId { get; set; }
    public int ZoneId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public ICollection<OverlappingReferenceLink> Links { get; set; } = new List<OverlappingReferenceLink>();
}
