namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScopedOptional;

internal sealed class ReviewFolder
{
    public int PartitionId { get; set; }
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<ReviewLink> Links { get; set; } = new List<ReviewLink>();
}
