namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryWriteTranslations;

internal sealed class StoredPatternNote
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
