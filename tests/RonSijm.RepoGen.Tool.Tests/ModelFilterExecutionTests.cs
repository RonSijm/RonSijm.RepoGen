using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ModelFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationStillPrecedesFilteredReadsAndColdEnumeration(bool mapped)
    {
        var (repository, data) = Fixture(mapped); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var task = (Task<IReadOnlyList<ModelFilterCard>>)repository.GetType().GetMethod("CardsAsync")!.Invoke(repository, [cancellation.Token])!;
        Func<Task> read = async () => await task;
        await read.Should().ThrowAsync<OperationCanceledException>();
        await using var stream = Invoke<IAsyncEnumerable<ModelFilterCard>>(repository, "StreamAsync").GetAsyncEnumerator(cancellation.Token);
        Func<Task> move = async () => await stream.MoveNextAsync();
        await move.Should().ThrowAsync<OperationCanceledException>();
        if (mapped) Store<ModelFilterRowSeed>(data, "ModelFilterRow").Count.Should().Be(5);
        else Store<ModelFilterRow>(data, "ModelFilterRow").Count.Should().Be(5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilterPrecedesNamedProjectionCountGroupsDictionaryAndPaging(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        (await Invoke<Task<IReadOnlyList<ModelFilterCard>>>(repository, "CardsAsync")).Select(row => row.Id).Should().Equal(1, 4, 5);
        (await Invoke<Task<ModelFilterCard?>>(repository, "GetAsync", 2)).Should().BeNull();
        (await Invoke<Task<int>>(repository, "CountAsync")).Should().Be(3);
        (await Invoke<Task<bool>>(repository, "ExistsAsync", 2)).Should().BeFalse();
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "TitlesAsync")).Keys.Should().BeEquivalentTo([1, 4, 5]);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "FoldersAsync")).Should().Equal(1, 1, 1);
        var page = await Invoke<Task<PagedResult<ModelFilterCard>>>(repository, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Select(row => row.Id).Should().Equal(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EachSourceIsFilteredBeforeJoinCorrelationSetOperationsAndPage(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "JoinedAsync")).Should().Equal(1);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "CorrelatedAsync")).Should().Equal(1);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "SelfAsync")).Should().Equal(1, 4, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "UnionAsync")).Should().Equal(1, 4, 5);
        var page = await Invoke<Task<PagedResult<int>>>(repository, "JoinedPageAsync", 1, 1);
        page.Items.Should().Equal(1); page.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BypassVisibilityAndIgnoreQueryFiltersRemainIndependent(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "IncludingDeletedAsync")).Should().Equal(1, 2, 4, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "IncludingHiddenAsync")).Should().Equal(1, 3, 4, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "AllAsync")).Should().Equal(1, 2, 3, 4, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "JoinedAllAsync")).Should().Equal(1, 2, 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ColdStreamsFilterDetachedInvocationSnapshotsWithoutDeletingSeeds(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        var stream = Invoke<IAsyncEnumerable<ModelFilterCard>>(repository, "StreamAsync");
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Id.Should().Be(1);
        if (mapped) Store<ModelFilterRowSeed>(data, "ModelFilterRow").Seed(new(4, "changed", true, false, 2), InMemoryDuplicateKeyBehavior.Replace);
        else Store<ModelFilterRow>(data, "ModelFilterRow").Seed(new() { Id = 4, Title = "changed", Deleted = true, FolderId = 2 }, InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Id.Should().Be(4);
        (await Invoke<Task<IReadOnlyList<ModelFilterCard>>>(repository, "CardsAsync")).Select(row => row.Id).Should().Equal(1, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "AllAsync")).Should().HaveCount(5);
        data.Clear();
        (await Invoke<Task<IReadOnlyList<ModelFilterCard>>>(repository, "CardsAsync")).Should().BeEmpty();
    }
}
