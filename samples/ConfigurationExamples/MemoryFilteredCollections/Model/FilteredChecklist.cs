namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredCollections;

internal sealed class FilteredChecklist
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Archived { get; set; }
    public ICollection<FilteredChecklistItem> Items { get; set; } = new List<FilteredChecklistItem>();
}
