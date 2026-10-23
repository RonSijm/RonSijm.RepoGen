namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredNavigation;

internal sealed class StoredFilteredShelf
{
    public int Id { get; set; }
    public bool Archived { get; set; }
    public List<StoredFilteredEntry> Entries { get; set; } = [];
}
