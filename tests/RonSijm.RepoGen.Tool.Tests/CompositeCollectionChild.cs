namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionChild
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Language { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public CompositeCollectionParent? Parent { get; set; }
}
