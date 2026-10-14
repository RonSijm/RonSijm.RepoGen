using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredNavigation;

public interface IFilteredShelfReader
{
    Task<IReadOnlyList<FilteredShelfCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredShelfCard>> AllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> WithTitleAsync(string title, CancellationToken cancellationToken = default);
    Task<PagedResult<FilteredShelfCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<FilteredShelfCard> StreamAsync(CancellationToken cancellationToken = default);
}
