namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryRelationshipComparers;

public interface IFolderEntryReader
{
    Task<IReadOnlyList<string?>> FolderNamesAsync(CancellationToken cancellationToken = default);
}
