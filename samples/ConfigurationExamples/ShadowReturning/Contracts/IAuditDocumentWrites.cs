using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public interface IAuditDocumentWrites
{
    Task<MutationResult<AuditDocumentChange>> RenameAsync(AuditDocumentRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<AuditDocumentChange>> RemoveAsync(AuditDocumentRequest request, CancellationToken cancellationToken = default);
}
