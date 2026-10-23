using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScalarEquality;

public interface IComparisonNotesWriter
{
    Task<CommandResult> StageCaptionAsync(ComparisonNoteChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<ComparisonNoteSeed>> RenameAsync(ComparisonNoteChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<ComparisonNoteSeed>> SetCaptionAsync(ComparisonNoteChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<int>> DeleteAsync(ComparisonNoteChange request, CancellationToken cancellationToken = default);
}
