namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;

internal sealed class StoredLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<StoredBoardLabel> Links { get; } = new List<StoredBoardLabel>();
    public IReadOnlyCollection<StoredBoard> Boards { get; } = new List<StoredBoard>();
}
