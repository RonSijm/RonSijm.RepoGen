namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public interface IVersionedChecklistItems
{
    Task<IReadOnlyList<VersionedItemCard>> ListAsync(int checklistId, CancellationToken cancellationToken = default);
}
