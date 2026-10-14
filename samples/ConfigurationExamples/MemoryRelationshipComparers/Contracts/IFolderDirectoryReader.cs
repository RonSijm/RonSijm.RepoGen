namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryRelationshipComparers;

public interface IFolderDirectoryReader
{
    Task<IReadOnlyList<FolderSummary>> SummariesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> ByExactCodeAsync(string code, CancellationToken cancellationToken = default);
    IAsyncEnumerable<FolderSummary> StreamAsync(CancellationToken cancellationToken = default);
}
