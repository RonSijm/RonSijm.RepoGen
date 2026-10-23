using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public interface IVersionedChecklistWriter
{
    Task<CommandResult<int>> ReplaceAsync(VersionedChecklistChange request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(VersionedChecklistChange request, CancellationToken cancellationToken = default);
}
