namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryShadowReads;

public sealed record RankedCategoryTotal(string Category, int Count, int RankTotal)
{
    public RankedCategoryTotal() : this("", 0, 0) { }
}
