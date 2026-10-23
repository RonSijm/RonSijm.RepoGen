namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCompoundParent
{
    public int Id { get; set; }
    public int Region { get; set; }
    public ICollection<MemoryCompoundChild> Children { get; set; } = new List<MemoryCompoundChild>();
}
