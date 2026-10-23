namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;

internal sealed class StoredBoard
{
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<StoredBoardLabel> Links { get; } = new List<StoredBoardLabel>();
    public IReadOnlyCollection<StoredLabel> Labels { get; } = new List<StoredLabel>();
}
