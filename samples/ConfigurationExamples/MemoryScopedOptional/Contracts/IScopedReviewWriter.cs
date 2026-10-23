using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public interface IScopedReviewWriter
{
    Task<CommandResult<ReviewFolderView>> ReplaceAsync(ChangeReviewReferences input, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(ChangeReviewReferences input, CancellationToken cancellationToken = default);
}
