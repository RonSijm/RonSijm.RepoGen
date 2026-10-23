namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionParent
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public ICollection<MemoryCollectionChild> Children { get; set; } = new List<MemoryCollectionChild>();
}
