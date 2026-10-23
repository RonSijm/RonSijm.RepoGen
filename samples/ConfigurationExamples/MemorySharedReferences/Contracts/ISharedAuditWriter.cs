using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public interface ISharedAuditWriter
{
    Task<CommandResult<AuditFolderView>> ReplaceAsync(ChangeAuditReferences input, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(ChangeAuditReferences input, CancellationToken cancellationToken = default);
}
