namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public interface ISavedEntryReader
{
    Task<IReadOnlyList<SavedEntrySeed>> AllAsync(CancellationToken cancellationToken = default);
}
