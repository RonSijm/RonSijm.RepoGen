namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeFilters;

internal sealed class StoredNativeNote
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
}
