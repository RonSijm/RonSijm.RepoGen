namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCollections;

internal sealed class StoredChecklistItem
{
    public int Id { get; set; }
    public int ChecklistId { get; set; }
    public StoredChecklist? Checklist { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
