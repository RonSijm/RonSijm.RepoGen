namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNestedProjections;

internal sealed class StoredBriefing
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Revision { get; set; }
    public bool Hidden { get; set; }
}
