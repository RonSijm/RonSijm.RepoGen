using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;

public interface IFilteredAuditWriter
{
    Task<CommandResult> StageAsync(FilteredAuditSeed request, CancellationToken cancellationToken = default);
}
