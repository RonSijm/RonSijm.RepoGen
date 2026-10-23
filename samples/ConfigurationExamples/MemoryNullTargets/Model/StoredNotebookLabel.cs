namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNullTargets;

internal sealed class StoredNotebookLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public IReadOnlyCollection<StoredNotebook> Notebooks { get; } = new List<StoredNotebook>();
}

