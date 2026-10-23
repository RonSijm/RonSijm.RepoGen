using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public interface IReviewBoardWriter
{
    Task<CommandResult<ReviewBoardView>> MergeAsync(ReviewBoardEdit request, CancellationToken cancellationToken = default);
    Task<CommandResult<ReviewBoardView>> ReplaceAsync(ReviewBoardEdit request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageReplaceAsync(ReviewBoardEdit request, CancellationToken cancellationToken = default);
}
