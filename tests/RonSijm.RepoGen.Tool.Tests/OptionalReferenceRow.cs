namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceRow
{
    public int OwnerId { get; set; }
    public int TargetId { get; set; }
    public int? ReviewerRegion { get; set; }
    public string? ReviewerCode { get; set; }
    public int? SourceId { get; set; }
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public OptionalReferenceOwner? Owner { get; set; }
    public OptionalReferenceTarget? Target { get; set; }
    public OptionalReferenceReviewer? Reviewer { get; set; }
    public OptionalReferenceSource? Source { get; set; }
}

