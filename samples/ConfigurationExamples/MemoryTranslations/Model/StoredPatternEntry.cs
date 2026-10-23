namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryTranslations;

internal sealed class StoredPatternEntry
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public int Price { get; set; }
    public bool Hidden { get; set; }
}
