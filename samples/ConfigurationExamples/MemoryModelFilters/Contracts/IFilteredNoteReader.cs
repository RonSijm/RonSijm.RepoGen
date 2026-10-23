using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelFilters;

public interface IFilteredNoteReader
{
    Task<IReadOnlyList<FilteredNoteCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredNoteCard>> IncludingDeletedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredNoteCard>> IncludingHiddenAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredNoteCard>> ActiveGroupsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<FilteredNoteCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<FilteredNoteCard> StreamAsync(CancellationToken cancellationToken = default);
}
