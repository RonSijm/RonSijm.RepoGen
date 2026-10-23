using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryShadowReads;

public interface IRankedNoteReader
{
    Task<IReadOnlyList<RankedNoteCard>> CardsAsync(int minimumRank, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankedNoteCard>> IncludingDeletedAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<RankedNoteCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankedCategoryTotal>> TotalsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<RankedNoteCard> StreamAsync(CancellationToken cancellationToken = default);
}
