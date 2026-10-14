namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkTarget
{
    public int Scope { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<AdditionalLinkRow> Links { get; set; } = [];
}
