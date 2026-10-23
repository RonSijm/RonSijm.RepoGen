using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public interface ISavedShelfWriter
{
    Task<CommandResult> StageArchiveAsync(int id, CancellationToken cancellationToken = default);
}
