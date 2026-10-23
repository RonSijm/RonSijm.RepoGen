namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;

public interface IReviewEntryReader
{
    Task<IReadOnlyList<ReviewEntryCard>> CardsAsync(CancellationToken cancellationToken = default);
}
