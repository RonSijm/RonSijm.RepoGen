namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadOwner
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<ManyReadTag> Tags { get; set; } = [];
}
