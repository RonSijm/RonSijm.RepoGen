namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCreateBatches;

internal sealed class IntakeEntry
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string Title { get; set; } = "";
    public IntakeFolder? Folder { get; set; }
}
