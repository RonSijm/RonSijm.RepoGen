namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<ShadowReadNote> Notes { get; set; } = [];
}
