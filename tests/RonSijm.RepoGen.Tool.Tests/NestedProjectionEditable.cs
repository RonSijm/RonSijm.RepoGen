namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionEditable
{
    public int Id { get; set; }
    public NestedProjectionDetail? Detail { get; set; }
    public List<NestedProjectionDetail> Items { get; set; } = [];
    public string[] Labels { get; set; } = [];
}
