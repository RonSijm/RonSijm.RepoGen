namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedCollections;

internal sealed class VersionedChecklist
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Counter { get; set; }
    public List<VersionedChecklistItem> Items { get; set; } = [];
}
