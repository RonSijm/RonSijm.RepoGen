namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteTarget
{
    public string Region { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<LinkWriteRow> Links { get; set; } = [];
    public List<LinkWriteOwner> Owners { get; set; } = [];
}
