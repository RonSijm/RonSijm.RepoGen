using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProjectionHelpers;

public interface IProjectionMemoReader
{
    Task<IReadOnlyList<MemoCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<MemoCard?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<MemoCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MemoCard> StreamAsync(CancellationToken cancellationToken = default);
}
