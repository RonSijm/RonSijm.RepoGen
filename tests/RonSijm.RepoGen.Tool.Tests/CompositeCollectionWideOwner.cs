namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionWideOwner
{
    public int Id { get; set; }
    public ICollection<CompositeCollectionWideChild> Children { get; set; } = new List<CompositeCollectionWideChild>();
}
