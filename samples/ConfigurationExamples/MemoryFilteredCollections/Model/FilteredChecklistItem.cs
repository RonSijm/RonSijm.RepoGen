namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredCollections;

internal sealed class FilteredChecklistItem
{
    public int Id { get; set; }
    public int ChecklistId { get; set; }
    public FilteredChecklist? Checklist { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Archived { get; set; }
    public bool Hidden { get; set; }
}
