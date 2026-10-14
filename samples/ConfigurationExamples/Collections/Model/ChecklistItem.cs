namespace RonSijm.RepoGen.ConfigurationExamples.Models.Collections;

public sealed class ChecklistItem
{
    public int Id { get; set; }
    public int ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public string InternalNote { get; set; } = "";
}
