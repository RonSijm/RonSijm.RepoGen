namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNullTargets;

internal sealed class StoredNotebook
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Hidden { get; set; }
    public IReadOnlyCollection<StoredNotebookLabel> Labels { get; } = new List<StoredNotebookLabel>();
}

