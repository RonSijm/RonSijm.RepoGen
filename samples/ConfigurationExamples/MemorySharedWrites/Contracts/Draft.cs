namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;

public sealed class Draft
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public bool Hidden { get; set; }
}
