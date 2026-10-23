namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCompoundChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int Region { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
}
