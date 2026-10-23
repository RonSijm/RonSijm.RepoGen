namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadLink
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string OwnerCode { get; set; } = "";
    public int? TagId { get; set; }
    public bool Hidden { get; set; }
}
