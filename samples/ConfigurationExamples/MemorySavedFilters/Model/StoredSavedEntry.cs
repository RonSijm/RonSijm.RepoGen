namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedFilters;

internal sealed class StoredSavedEntry
{
    public int Id { get; set; }
    public int? ShelfId { get; set; }
    public string Title { get; set; } = "";
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
    public StoredSavedShelf? Shelf { get; set; }
}
