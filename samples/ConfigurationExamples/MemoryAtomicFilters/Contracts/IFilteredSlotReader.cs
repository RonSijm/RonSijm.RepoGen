namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;

public interface IFilteredSlotReader
{
    Task<IReadOnlyList<FilteredSlotView>> ListAsync(CancellationToken cancellationToken = default);
}
