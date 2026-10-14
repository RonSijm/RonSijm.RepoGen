namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelWrites;

internal sealed class StoredFilteredDraft
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int Version { get; set; }
    public bool Archived { get; set; }
    public bool Hidden { get; set; }
}
