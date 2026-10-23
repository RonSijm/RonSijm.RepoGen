using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryResultCacheTests
{
    private static readonly string[] Tags = ["dependency"];

    [Fact]
    public async Task CacheClonesMutableResultsAndHonorsTagInvalidation()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new MemoryRepositoryResultCache(memory);
        var source = new List<string> { "original" };
        var first = await cache.GetOrCreateAsync("query", Tags, new RepositoryCacheOptions(), _ => Task.FromResult(source));
        first[0] = "caller mutation";
        var next = await cache.GetOrCreateAsync<List<string>>("query", Tags, new RepositoryCacheOptions(), _ => throw new InvalidOperationException("Should be a hit"));
        next.Should().Equal("original");
        next[0] = "cached caller mutation";
        (await cache.GetOrCreateAsync<List<string>>("query", Tags, new RepositoryCacheOptions(), _ => throw new InvalidOperationException("Should be a hit"))).Should().Equal("original");
        await cache.InvalidateAsync(Tags);
        (await cache.GetOrCreateAsync("query", Tags, new RepositoryCacheOptions(), _ => Task.FromResult(new List<string> { "new" }))).Should().Equal("new");
    }

    [Fact]
    public async Task AnInflightFillCannotResurrectAnInvalidatedEntry()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var complete = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = cache.GetOrCreateAsync("query", Tags, new RepositoryCacheOptions(), _ => complete.Task);
        await cache.InvalidateAsync(Tags);
        complete.SetResult("old");
        (await old).Should().Be("old");
        (await cache.GetOrCreateAsync("query", Tags, new RepositoryCacheOptions(), _ => Task.FromResult("new"))).Should().Be("new");
        (await cache.GetOrCreateAsync<string>("query", Tags, new RepositoryCacheOptions(), _ => throw new InvalidOperationException("Should be a hit"))).Should().Be("new");
    }
}