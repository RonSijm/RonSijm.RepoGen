namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceLink
{
    public int Scope { get; set; }
    public int Zone { get; set; }
    public int OwnerId { get; set; }
    public OverlappingReferenceOwner? Owner { get; set; }
    public int TargetId { get; set; }
    public OverlappingReferenceTarget? Target { get; set; }
    public string ActorCode { get; set; } = "";
    public OverlappingReferenceActor? Actor { get; set; }
    public string ReviewerCode { get; set; } = "";
    public OverlappingReferenceActor? Reviewer { get; set; }
    public int SourceId { get; set; }
    public OverlappingReferenceSource? Source { get; set; }
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
