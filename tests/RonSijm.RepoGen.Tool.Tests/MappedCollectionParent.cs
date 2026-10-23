namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionParent
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Counter { get; set; }
    public List<MappedCollectionChild> Children { get; set; } = [];
}
