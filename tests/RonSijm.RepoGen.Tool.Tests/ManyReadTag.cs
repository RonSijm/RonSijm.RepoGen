namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<ManyReadOwner> Owners { get; set; } = [];
}
