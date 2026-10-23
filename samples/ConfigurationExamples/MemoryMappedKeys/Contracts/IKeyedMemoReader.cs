namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedKeys;

public interface IKeyedMemoReader
{
    Task<IReadOnlyList<KeyedMemoCard>> CardsAsync(CancellationToken cancellationToken = default);
}
