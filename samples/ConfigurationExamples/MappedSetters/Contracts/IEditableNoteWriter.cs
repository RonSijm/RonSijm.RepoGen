using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedSetters;

public interface IEditableNoteWriter
{
    Task<CommandResult<EditableNoteSeed>> EditAsync(EditNoteRequest request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageEditAsync(EditNoteRequest request, CancellationToken cancellationToken = default);
    Task<int> ExecuteEditAsync(EditNoteRequest request, CancellationToken cancellationToken = default);
}
