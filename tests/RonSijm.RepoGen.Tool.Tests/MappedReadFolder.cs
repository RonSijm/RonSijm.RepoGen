namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadFolder
{
    private bool hidden = true;
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<MappedReadItem> Items { get; set; } = [];
    public bool IsHidden() => hidden;
}
