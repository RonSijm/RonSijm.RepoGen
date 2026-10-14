namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkTarget
{
    public int PartitionId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<SharedLinkRow> Links { get; set; } = [];
}
