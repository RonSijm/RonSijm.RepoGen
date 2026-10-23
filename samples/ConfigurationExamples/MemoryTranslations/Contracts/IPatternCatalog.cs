namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryTranslations;

public interface IPatternCatalog
{
    Task<IReadOnlyList<PatternCard>> SearchAsync(PatternSearch request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> SummariesAsync(string pattern, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> EscapedAsync(string pattern, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PatternTotals>> TotalsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<PatternCard> StreamAsync(PatternSearch request, CancellationToken cancellationToken = default);
}
