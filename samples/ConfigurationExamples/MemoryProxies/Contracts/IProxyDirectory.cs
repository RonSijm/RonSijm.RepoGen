using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyDirectory;

public interface IProxyDirectory
{
    Task<DirectoryCard?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<DirectoryCard>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> TotalPointsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<DirectoryCard> StreamAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> InternalTagsAsync(CancellationToken cancellationToken = default);
}
