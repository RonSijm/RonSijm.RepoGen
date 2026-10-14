namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkOwner
{
    public int SpaceId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public List<SharedLinkRow> Links { get; set; } = [];
}
