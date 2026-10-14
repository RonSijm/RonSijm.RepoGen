namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionParent
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<CompositeCollectionChild> Children { get; set; } = new List<CompositeCollectionChild>();
}
