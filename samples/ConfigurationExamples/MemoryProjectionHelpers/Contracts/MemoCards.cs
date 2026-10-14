namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProjectionHelpers;

/// <summary>Application-owned scalar-to-DTO mapper, used by the original production projection.</summary>
public static class MemoCards
{
    public static MemoCard Create(int id, string text, int revision)
        => new(id, text.Trim().ToUpperInvariant(), revision, text.Length);
}
