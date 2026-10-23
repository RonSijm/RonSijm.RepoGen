namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultipleSharedLinkRow
{
    public int Scope1 { get; set; }
    public int Scope2 { get; set; }
    public int OwnerId { get; set; }
    public string TargetCode { get; set; } = "";
    public string Caption { get; set; } = "";
    public MultipleSharedLinkOwner? Owner { get; set; }
    public MultipleSharedLinkTarget? Target { get; set; }
}
