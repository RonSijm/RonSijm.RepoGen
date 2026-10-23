using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;

public interface ICatalogBrowse
{
    Task<IReadOnlyList<CatalogGroupCard>> BrowseAsync(CancellationToken cancellationToken = default);
    Task<CatalogGroupCard?> DetailAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<CatalogGroupCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> RawCountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogEntryCard>> ItemsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<CatalogGroupCard> StreamAsync(CancellationToken cancellationToken = default);
}
