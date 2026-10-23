namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;

public sealed class BatchDocument
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int OwnerId { get; set; }
    public string? Title { get; set; }
    public int Version { get; set; }
    public bool Archived { get; set; }
}
