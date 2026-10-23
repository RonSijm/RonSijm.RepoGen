using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;

public interface IFilteredSlotWriter
{
    Task<CommandResult<FilteredSlotView>> EnsureAsync(FilteredSlotInput input, CancellationToken cancellationToken = default);
    Task<CommandResult<FilteredSlotView>> PutAsync(FilteredSlotInput input, CancellationToken cancellationToken = default);
    Task<CommandResult<FilteredSlotView>> ArchivePutAsync(FilteredSlotInput input, CancellationToken cancellationToken = default);
    Task<CommandResult> StageValueAsync(FilteredSlotInput input, CancellationToken cancellationToken = default);
}
