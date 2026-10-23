using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeFilters;

public interface INativeNoteWriter
{
    Task<MutationResult<NativeNoteCard>> UpdateAsync(NativeNoteInput input, CancellationToken cancellationToken = default);
    Task<MutationResult<NativeNoteCard>> ArchiveAllAsync(CancellationToken cancellationToken = default);
    Task<MutationResult<NativeNoteCard>> ConsumeAsync(NativeNoteInput input, CancellationToken cancellationToken = default);
    Task<CommandResult> StageTitleAsync(NativeNoteInput input, CancellationToken cancellationToken = default);
}
