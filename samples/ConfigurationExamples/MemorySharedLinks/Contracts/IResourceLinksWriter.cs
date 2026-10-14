using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;

public interface IResourceLinksWriter
{
    Task<CommandResult<FolderView>> MergeResourcesAsync(ChangeFolderResources request, CancellationToken cancellationToken = default);
    Task<CommandResult<FolderView>> ReplaceResourcesAsync(ChangeFolderResources request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageResourcesAsync(ChangeFolderResources request, CancellationToken cancellationToken = default);
}
