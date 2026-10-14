namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;

public sealed class JournalEntry
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}
