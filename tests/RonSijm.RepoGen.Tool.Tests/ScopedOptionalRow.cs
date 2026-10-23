namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalRow
{
    public int Scope { get; set; }
    public int Zone { get; set; }
    public int OwnerId { get; set; }
    public ScopedOptionalOwner? Owner { get; set; }
    public int TargetId { get; set; }
    public ScopedOptionalTarget? Target { get; set; }
    public string ActorRegion { get; set; } = "";
    public string ActorCode { get; set; } = "";
    public ScopedOptionalReviewer? Actor { get; set; }
    public string? ReviewerRegion { get; set; }
    public string? ReviewerCode { get; set; }
    public ScopedOptionalReviewer? Reviewer { get; set; }
    public int? OriginId { get; set; }
    public ScopedOptionalOrigin? Origin { get; set; }
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
