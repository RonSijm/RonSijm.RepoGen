using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ReturningShadowCache(IRepositoryResultCache inner) : IRepositoryResultCache
{
    internal int Invalidations { get; private set; }
    public Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options,
        Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default) => inner.GetOrCreateAsync(key, tags, options, factory, cancellationToken);
    public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
    {
        Invalidations++; return inner.InvalidateAsync(tags, cancellationToken);
    }
}
