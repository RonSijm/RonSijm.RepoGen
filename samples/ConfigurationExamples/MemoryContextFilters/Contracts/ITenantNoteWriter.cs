using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;

public interface ITenantNoteWriter
{
    Task<CommandResult<TenantNoteCard>> RenameAsync(TenantNoteRequest input, CancellationToken cancellationToken = default);
    Task<CommandResult> StageRenameAsync(TenantNoteRequest input, CancellationToken cancellationToken = default);
    Task<CommandResult> ArchiveAsync(TenantNoteRequest input, CancellationToken cancellationToken = default);
}
