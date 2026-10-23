namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedResults;

internal sealed class SavedNote
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public SavedNotebook? Notebook { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Archived { get; set; }
}
