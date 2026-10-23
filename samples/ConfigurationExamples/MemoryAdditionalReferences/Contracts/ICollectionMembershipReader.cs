namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;

public interface ICollectionMembershipReader
{
    Task<CollectionView?> FindAsync(int id, CancellationToken cancellationToken = default);
}
