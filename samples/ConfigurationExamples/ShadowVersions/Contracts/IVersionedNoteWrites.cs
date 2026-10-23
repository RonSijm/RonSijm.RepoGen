using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public interface IVersionedNoteWrites
{
    Task<CommandResult<NoteVersionView>> RenameAsync(NoteVersionChange request, CancellationToken cancellationToken = default);
    Task<MutationResult<NoteVersionView>> RenameNativeAsync(NoteVersionChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<IReadOnlyList<NoteVersionView>>> RenameManyAsync(NoteVersionBatch request, CancellationToken cancellationToken = default);
}
