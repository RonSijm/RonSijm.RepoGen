using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReads;

public interface IMappedMemoReader
{
    Task<IReadOnlyList<MappedMemoCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<MappedMemoCard?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MappedMemoCard>> SortedAsync(string field, bool descending, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MappedMemoCard>> RecentAsync(int minimumRevision, CancellationToken cancellationToken = default);
    Task<PagedResult<MappedMemoCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
