namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryExcludedCollectionParent
{
    public int Id { get; set; }
    public ICollection<MemoryExcludedCollectionContext.NestedChild> NestedChildren { get; set; } = new List<MemoryExcludedCollectionContext.NestedChild>();
    public ICollection<MemoryGenericCollectionChild<int>> GenericChildren { get; set; } = new List<MemoryGenericCollectionChild<int>>();
}
