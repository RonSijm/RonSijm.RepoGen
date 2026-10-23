using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedWrites;

public interface IRevisionNoteWriter
{
    Task<CommandResult<RevisionCard>> RenameAsync(RevisionChange request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageRenameAsync(RevisionChange request, CancellationToken cancellationToken = default);
}
