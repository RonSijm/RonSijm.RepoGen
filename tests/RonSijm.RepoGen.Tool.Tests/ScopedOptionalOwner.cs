namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalOwner
{
    public int PartitionId { get; set; }
    public int ZoneId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public ICollection<ScopedOptionalRow> Links { get; set; } = new List<ScopedOptionalRow>();
}
