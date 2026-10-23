namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public interface ISharedAuditReader
{
    Task<AuditFolderView?> FindAsync(AuditFolderKey key, CancellationToken cancellationToken = default);
}
