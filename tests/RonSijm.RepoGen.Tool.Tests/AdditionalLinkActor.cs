namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkActor
{
    public int Id { get; set; }
    public string Region { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<AdditionalLinkRow> Links { get; set; } = [];
}
