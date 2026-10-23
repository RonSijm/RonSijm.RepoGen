using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;

public interface IShelfCatalog
{
    Task<IReadOnlyList<ShelfCard>> BrowseAsync(CancellationToken cancellationToken = default);
    Task<ShelfCard?> DetailAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<ShelfCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> RawCountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShelfItemCard>> ItemsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ShelfCard> StreamAsync(CancellationToken cancellationToken = default);
}
