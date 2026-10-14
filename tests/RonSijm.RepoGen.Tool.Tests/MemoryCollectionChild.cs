namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public MemoryCollectionParent? Parent { get; set; }
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
