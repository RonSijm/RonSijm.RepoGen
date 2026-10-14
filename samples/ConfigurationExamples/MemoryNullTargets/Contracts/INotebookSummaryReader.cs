using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNullTargets;

public interface INotebookSummaryReader
{
    Task<IReadOnlyList<NotebookSummary>> SummariesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> HasMissingAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<NotebookSummary>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<NotebookSummary> StreamAsync(CancellationToken cancellationToken = default);
}
