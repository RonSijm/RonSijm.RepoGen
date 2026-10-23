namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkRow
{
    public int Scope { get; set; }
    public int OwnerId { get; set; }
    public int TargetId { get; set; }
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public SharedLinkOwner? Owner { get; set; }
    public SharedLinkTarget? Target { get; set; }
}
