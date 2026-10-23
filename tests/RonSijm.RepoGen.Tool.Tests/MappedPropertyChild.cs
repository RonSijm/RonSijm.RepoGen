namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyChild
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Title { get; set; } = "child";
    public int Counter { get; set; }
}
