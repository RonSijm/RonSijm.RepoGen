using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;

public interface IBriefingReader
{
    Task<IReadOnlyList<BriefingCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<BriefingCard?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<BriefingCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<BriefingCard> StreamAsync(CancellationToken cancellationToken = default);
}
