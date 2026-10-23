namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public interface IReviewBoardReader
{
    Task<IReadOnlyList<ReviewBoardView>> BoardsAsync(CancellationToken cancellationToken = default);
}
