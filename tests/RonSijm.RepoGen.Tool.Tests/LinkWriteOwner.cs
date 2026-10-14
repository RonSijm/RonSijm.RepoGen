namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteOwner
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public List<LinkWriteRow> Links { get; set; } = [];
    public List<LinkWriteTarget> Tags { get; set; } = [];
}
