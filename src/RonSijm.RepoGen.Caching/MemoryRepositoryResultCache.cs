using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Caching;

/// <summary>A singleton adapter over an application-owned IMemoryCache. Tag generations prevent invalidated in-flight fills from being reused.</summary>
/// <remarks>Does not own/dispose the supplied cache. Keeps a generation per encountered scope/tag; configure a bounded cache for entries. Concurrent misses may run multiple factories.</remarks>
public sealed class MemoryRepositoryResultCache : IRepositoryResultCache
{
    private readonly IMemoryCache memory;
    private readonly bool cloneResults;
    private readonly JsonSerializerOptions? json;
    private readonly ConcurrentDictionary<string, Generation> generations = new(StringComparer.Ordinal);

    public MemoryRepositoryResultCache(IMemoryCache memory, bool cloneResults = true, JsonSerializerOptions? json = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.cloneResults = cloneResults;
        this.json = json;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options,
        Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();
        var partitioned = "RonSijm.RepoGen:" + key;
        if (memory.TryGetValue(partitioned, out Entry<T>? hit) && hit is not null && Current(hit.Versions))
        {
            return hit.Bytes is null ? hit.Value! : JsonSerializer.Deserialize<T>(hit.Bytes, json)!;
        }

        var snapshot = tags.Distinct(StringComparer.Ordinal).Select(tag =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            var version = generations.GetOrAdd(tag, static _ => new Generation());
            return (version, Volatile.Read(ref version.Value));
        }).ToArray();
        var value = await factory(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (Current(snapshot))
        {
            var entry = new Entry<T>(cloneResults ? default : value, cloneResults ? JsonSerializer.SerializeToUtf8Bytes(value, json) : null, snapshot);
            var policy = new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = options.AbsoluteExpiration ?? (options.SlidingExpiration is null ? TimeSpan.FromMinutes(5) : null), SlidingExpiration = options.SlidingExpiration };
            memory.Set(partitioned, entry, policy);
        }

        return value;
    }

    public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var tag in tags.Distinct(StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            Interlocked.Increment(ref generations.GetOrAdd(tag, static _ => new Generation()).Value);
        }

        return Task.CompletedTask;
    }

    private static bool Current((Generation Generation, long Version)[] versions) => versions.All(static value => Volatile.Read(ref value.Generation.Value) == value.Version);

    private sealed class Generation
    {
        public long Value;
    }

    private sealed record Entry<T>(T? Value, byte[]? Bytes, (Generation Generation, long Version)[] Versions);
}