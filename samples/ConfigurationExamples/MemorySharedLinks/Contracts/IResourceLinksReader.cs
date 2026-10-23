namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;

public interface IResourceLinksReader
{
    Task<FolderView?> FindAsync(FolderKey key, CancellationToken cancellationToken = default);
}
