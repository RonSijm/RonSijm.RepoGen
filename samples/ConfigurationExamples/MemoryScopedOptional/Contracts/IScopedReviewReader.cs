namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public interface IScopedReviewReader
{
    Task<ReviewFolderView?> FindAsync(ReviewFolderKey key, CancellationToken cancellationToken = default);
}
