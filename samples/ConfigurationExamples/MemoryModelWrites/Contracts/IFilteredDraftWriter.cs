using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;

public interface IFilteredDraftWriter
{
    Task<CommandResult<FilteredDraftCard>> RenameAsync(RenameFilteredDraft request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageTextAsync(RenameFilteredDraft request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageArchiveAsync(int id, CancellationToken cancellationToken = default);
    Task<CommandResult<FilteredDraftCard>> ArchiveAndReadAsync(int id, CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteArchiveAsync(int id, CancellationToken cancellationToken = default);
    Task<CommandResult<FilteredDraftCard>> CreateAsync(FilteredDraftSeed request, CancellationToken cancellationToken = default);
}
