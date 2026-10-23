using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLoadingPolicies;

public interface IExplicitCatalogReader
{
    Task<IReadOnlyList<CatalogGroupCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<CatalogGroupDetail?> DetailAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogEntryCard>> EntriesAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<CatalogGroupCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<CatalogGroupCard> StreamAsync(CancellationToken cancellationToken = default);
}
