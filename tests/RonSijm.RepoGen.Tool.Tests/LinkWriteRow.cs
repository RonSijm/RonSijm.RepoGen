namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteRow
{
    public int OwnerTenantId { get; set; }
    public int OwnerId { get; set; }
    public string TargetRegion { get; set; } = "";
    public string TargetCode { get; set; } = "";
    public string Note { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public LinkWriteOwner? Owner { get; set; }
    public LinkWriteTarget? Target { get; set; }
}
