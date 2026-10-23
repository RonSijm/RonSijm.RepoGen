namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedKeys;

public interface IKeyedFolderReader
{
    Task<IReadOnlyList<KeyedFolderCard>> CardsAsync(CancellationToken cancellationToken = default);
}
