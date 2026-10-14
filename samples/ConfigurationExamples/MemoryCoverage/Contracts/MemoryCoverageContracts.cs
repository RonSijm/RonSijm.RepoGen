namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCoverage;

public sealed record ReviewedItem(int Id, string Name);
public sealed record ReviewedItemWithReviewer(int Id, string Name, string Reviewer);

public sealed class ReviewedEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public int? ReviewerId { get; set; }
    public EntryReviewer? Reviewer { get; set; }
}
public sealed class EntryReviewer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
