using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public interface ICapturedNoteWrites
{
    Task<MutationResult<CapturedNoteChange>> RenameAsync(CapturedNoteRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<CapturedNoteChange>> RemoveAsync(CapturedNoteRequest request, CancellationToken cancellationToken = default);
}
