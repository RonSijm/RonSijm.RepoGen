namespace RonSijm.RepoGen.Abstractions;

/// <summary>Pluggable result caching. Implementations must associate every dependency tag with the entry.</summary>
/// <remarks>Use tag generations or equivalent coordination to prevent in-flight fills from resurrecting invalidated entries.</remarks>
public interface IRepositoryResultCache
{
    Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options,
        Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default);

    Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default);
}

public sealed record RepositoryCacheOptions(TimeSpan? AbsoluteExpiration = null, TimeSpan? SlidingExpiration = null);