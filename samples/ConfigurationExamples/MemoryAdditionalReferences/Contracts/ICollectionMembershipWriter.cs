using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;

public interface ICollectionMembershipWriter
{
    Task<CommandResult<CollectionView>> MergeAsync(ChangeMemberships request, CancellationToken cancellationToken = default);
    Task<CommandResult<CollectionView>> ReplaceAsync(ChangeMemberships request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(ChangeMemberships request, CancellationToken cancellationToken = default);
}
