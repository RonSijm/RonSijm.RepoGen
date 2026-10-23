namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;

/// <summary>Application result mapper. Production calls it; fixtures explicitly configure an equivalent recipe.</summary>
public static class BriefingCards
{
    public static BriefingCard Create(int id, string title, int revision) => new(id,
        new(title.Trim().ToUpperInvariant(), revision),
        new List<BriefingFact> { new("length", title.Length), new("revision", revision) },
        title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToUpperInvariant()).ToArray());
}
