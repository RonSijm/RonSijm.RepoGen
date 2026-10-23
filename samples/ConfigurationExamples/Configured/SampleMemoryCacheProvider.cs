using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace RonSijm.RepoGen.ConfigurationExamples.Configured;

// A deliberately small, single-process adapter for this demonstration, not a distributed consistency policy.
internal sealed class SampleMemoryCacheProvider(IMemoryCache cache) : IRepositoryCacheProvider
{
    private readonly ConcurrentDictionary<string, long> versions = new(StringComparer.Ordinal);
    public int FactoryCalls { get; private set; }

    public Task<TResult> GetOrCreateAsync<TResult>(string key, string invalidationTag,
        Func<CancellationToken, Task<TResult>> valueFactory, CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(key, invalidationTag, new RepositoryCacheEntryOptions(), valueFactory, cancellationToken);

    public async Task<TResult> GetOrCreateAsync<TResult>(string key, string invalidationTag, RepositoryCacheEntryOptions options,
        Func<CancellationToken, Task<TResult>> valueFactory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = versions.GetOrAdd(invalidationTag, 0);
        var scopedKey = invalidationTag + ":" + version.ToString(CultureInfo.InvariantCulture) + ":" + key;
        if (cache.TryGetValue(scopedKey, out TResult? cached))
        {
            return cached!;
        }

        FactoryCalls++;
        var value = await valueFactory(cancellationToken);
        cache.Set(scopedKey, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(5),
            SlidingExpiration = options.SlidingExpiration
        });
        return value;
    }

    public Task InvalidateTagAsync(string invalidationTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        versions.AddOrUpdate(invalidationTag, 1, static (_, version) => version + 1);
        return Task.CompletedTask;
    }
}