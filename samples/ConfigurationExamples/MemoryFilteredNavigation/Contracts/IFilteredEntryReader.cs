namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredNavigation;

public interface IFilteredEntryReader
{
    Task<IReadOnlyList<FilteredEntryCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredEntryCard>> AllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FilteredEntryCard>> FromAsync(CancellationToken cancellationToken = default);
}
