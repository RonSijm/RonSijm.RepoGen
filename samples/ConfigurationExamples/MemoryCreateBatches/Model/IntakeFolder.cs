namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCreateBatches;

internal sealed class IntakeFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<IntakeEntry> Entries { get; set; } = [];
}
