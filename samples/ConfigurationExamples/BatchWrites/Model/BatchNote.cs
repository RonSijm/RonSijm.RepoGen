namespace RonSijm.RepoGen.ConfigurationExamples.Models.BatchWrites;

public sealed class BatchNote
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int OwnerId { get; set; }
    public string? Title { get; set; }
    public int Version { get; set; } = 1;
}
