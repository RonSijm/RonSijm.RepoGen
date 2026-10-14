using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;

public interface IPatternNoteWriter
{
    Task<CommandResult> StageRenameAsync(PatternNoteRename request, CancellationToken cancellationToken = default);
    Task<CommandResult<PatternNoteCard>> RenameAsync(PatternNoteRename request, CancellationToken cancellationToken = default);
    Task<int> DeleteMatchingAsync(string pattern, CancellationToken cancellationToken = default);
}
