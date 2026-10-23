namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCollections;

internal sealed class StoredChecklist
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<StoredChecklistItem> Items { get; set; } = new List<StoredChecklistItem>();
}
