namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryImmediateWrites;

internal sealed class QueuedEnvelope
{
    public int Id { get; set; }
    public string Queue { get; set; } = "";
    public string Body { get; set; } = "";
    public int Attempts { get; set; }
    public int PreviousAttempts { get; set; }
    public int Version { get; set; }
    public bool Hidden { get; set; }
}
