namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkRow
{
    public int Scope { get; set; }
    public int OwnerId { get; set; }
    public int TargetId { get; set; }
    public string ActorRegion { get; set; } = "";
    public string ActorCode { get; set; } = "";
    public int SourceId { get; set; }
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public AdditionalLinkOwner? Owner { get; set; }
    public AdditionalLinkTarget? Target { get; set; }
    public AdditionalLinkActor? Actor { get; set; }
    public AdditionalLinkSource? Source { get; set; }
}
