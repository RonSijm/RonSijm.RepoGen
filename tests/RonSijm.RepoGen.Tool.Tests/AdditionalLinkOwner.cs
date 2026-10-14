namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkOwner
{
    public int Scope { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<AdditionalLinkRow> Links { get; set; } = [];
}
