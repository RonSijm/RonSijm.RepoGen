namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultipleSharedLinkTarget
{
    public int Id { get; set; }
    public int Region { get; set; }
    public string Code { get; set; } = "";
    public int Segment { get; set; }
    public List<MultipleSharedLinkRow> Links { get; set; } = [];
}
