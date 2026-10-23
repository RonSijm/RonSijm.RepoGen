namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredNavigation;

internal sealed class StoredFilteredEntry
{
    public int Id { get; set; }
    public int? ShelfId { get; set; }
    public string Title { get; set; } = "";
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
    public StoredFilteredShelf? Shelf { get; set; }
}
