using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;

public interface IReviewEntryWriter
{
    Task<CommandResult<IReadOnlyList<ReviewEntryCard>>> ReviseAsync(ReviewEntriesRequest request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageReviseAsync(ReviewEntriesRequest request, CancellationToken cancellationToken = default);
}
