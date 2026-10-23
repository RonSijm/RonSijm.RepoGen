using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningShadowTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidationWaitsForSuccessAndForTheEnclosingCommit(bool shared)
    {
        await using var fixture = await CreateAsync(); using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new ReturningShadowCache(new MemoryRepositoryResultCache(memory));
        await using var work = new RepositoryUnitOfWork<ReturningShadowContext>(new ReturningShadowLeaseProvider(fixture.Context));
        var operations = Operations(Compile(fixture.Context, Builder(leases: true, caching: true)), work, cache: cache);
        (await Quantities()).Should().Equal(1, 1);
        if (shared)
        {
            await using var transaction = await work.BeginTransactionAsync();
            await ((IReturningShadowWrites)operations).AdjustAsync(new("item", 2, "!", 1));
            cache.Invalidations.Should().Be(0);
            (await Quantities()).Should().Equal(9, 11); // transaction-local reads bypass shared cache
            await transaction.CommitAsync();
        }
        else await ((IReturningShadowWrites)operations).AdjustAsync(new("item", 2, "!", 1));
        cache.Invalidations.Should().Be(1); (await Quantities()).Should().Equal(9, 11);
        Func<Task> fail = async () => await ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await fail.Should().ThrowAsync<InvalidOperationException>();
        cache.Invalidations.Should().Be(1); (await Quantities()).Should().Equal(9, 11);
        (await ((IReturningShadowWrites)operations).RemoveAsync(new("missing", 0, "", 1))).AffectedRows.Should().Be(0);
        cache.Invalidations.Should().Be(1);
        Task<IReadOnlyList<int>> Quantities() => ReturningWriteTests.Call<Task<IReadOnlyList<int>>>(operations, "QuantitiesAsync", CancellationToken.None);
    }
}
