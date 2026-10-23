namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedResults;

internal sealed class SavedNotebook
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<SavedNote> Notes { get; set; } = new List<SavedNote>();
}
