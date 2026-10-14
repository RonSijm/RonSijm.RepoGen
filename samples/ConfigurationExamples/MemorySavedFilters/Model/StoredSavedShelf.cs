namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedFilters;

internal sealed class StoredSavedShelf
{
    public int Id { get; set; }
    public bool Archived { get; set; }
    public List<StoredSavedEntry> Entries { get; set; } = [];
}
