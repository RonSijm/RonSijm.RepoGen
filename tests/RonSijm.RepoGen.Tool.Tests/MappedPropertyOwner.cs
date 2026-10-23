namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyOwner
{
    public int Id { get; set; }
    public string Title { get; set; } = "owner";
    public List<MappedPropertyChild> Children { get; set; } = [];
}
