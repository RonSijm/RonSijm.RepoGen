namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;

public interface IFilteredDraftReader
{
    Task<IReadOnlyList<FilteredDraftCard>> CardsAsync(CancellationToken cancellationToken = default);
}
