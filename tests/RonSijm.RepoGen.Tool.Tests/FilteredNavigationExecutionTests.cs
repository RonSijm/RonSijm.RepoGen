using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredNavigationTestHarness;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FiltersPrecedeCollectionAggregatesOptionalReferencesAndEveryFromRoot(bool mapped)
    {
        var (rows, folders, _) = Fixture(mapped);
        var cards = await Invoke<Task<IReadOnlyList<FilteredNavigationCard>>>(rows, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 4, 5);
        cards.Select(row => row.VisibleFolderId).Should().Equal(1, null, null);
        (await Invoke<Task<IReadOnlyList<int>>>(rows, "VisibleFolderAsync")).Should().Equal(1);
        (await Invoke<Task<IReadOnlyList<FilteredNavigationCard>>>(rows, "FromAsync")).Should().Equal(cards);
        (await Invoke<Task<IReadOnlyList<int>>>(rows, "JoinedAsync")).Should().Equal(1);
        var related = (await Invoke<Task<IReadOnlyList<FilteredNavigationFolderCard>>>(folders, "CardsAsync")).Single();
        related.Id.Should().Be(1); related.Count.Should().Be(2); related.Titles.Should().Equal("one", "hidden");
        (await Invoke<Task<IReadOnlyList<int>>>(folders, "AnyAsync", "deleted")).Should().BeEmpty();
        (await Invoke<Task<IReadOnlyList<int>>>(folders, "AnyAsync", "hidden")).Should().Equal(1);
        var loaded = (await Invoke<Task<IReadOnlyList<FilteredNavigationFolderCard>>>(folders, "LoadedAsync")).Single();
        loaded.Titles.Should().Equal(related.Titles);
        var page = await Invoke<Task<PagedResult<FilteredNavigationCard>>>(rows, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().VisibleFolderId.Should().BeNull();
        var folderPage = await Invoke<Task<PagedResult<FilteredNavigationFolderCard>>>(folders, "PageAsync", 1, 1);
        folderPage.TotalCount.Should().Be(1); folderPage.Items.Single().Count.Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IgnoreBypassesAllGraphFiltersButNotRootVisibilityAndLeavesSeedsUntouched(bool mapped)
    {
        var (rows, folders, data) = Fixture(mapped); data.CaptureBaseline();
        var all = await Invoke<Task<IReadOnlyList<FilteredNavigationCard>>>(rows, "AllAsync");
        all.Select(row => row.Id).Should().Equal(1, 2, 4, 5); all.Select(row => row.VisibleFolderId).Should().Equal(1, 1, 2, null);
        var allFolders = await Invoke<Task<IReadOnlyList<FilteredNavigationFolderCard>>>(folders, "AllAsync");
        allFolders.Select(row => row.Count).Should().Equal(3, 1);
        allFolders[0].Titles.Clear();
        (await Invoke<Task<IReadOnlyList<FilteredNavigationFolderCard>>>(folders, "CardsAsync")).Single().Titles.Should().Equal("one", "hidden");
        if (mapped) ModelFilterTestHarness.Store<ModelFilterRowSeed>(data, "ModelFilterRow").Count.Should().Be(5);
        else ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").Count.Should().Be(5);
        data.Clear(); data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<FilteredNavigationCard>>>(rows, "CardsAsync")).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ColdStreamsKeepOneDetachedGraphSnapshotAndCancellationPrecedesFiltering(bool mapped)
    {
        var (rows, folders, data) = Fixture(mapped);
        var cold = Invoke<IAsyncEnumerable<FilteredNavigationCard>>(rows, "StreamAsync");
        if (mapped) ModelFilterTestHarness.Store<ModelFilterFolderSeed>(data, "ModelFilterFolder").Seed(new(1, true), InMemoryDuplicateKeyBehavior.Replace);
        else ModelFilterTestHarness.Store<ModelFilterFolder>(data, "ModelFilterFolder").Seed(new() { Id = 1, Archived = true }, InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = cold.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.VisibleFolderId.Should().BeNull(); data.Clear();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Id.Should().Be(4);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> read = async () => await (Task<IReadOnlyList<FilteredNavigationCard>>)rows.GetType().GetMethod("CardsAsync")!.Invoke(rows, [cancelled.Token])!;
        await read.Should().ThrowAsync<OperationCanceledException>();
        await using var stream = Invoke<IAsyncEnumerable<FilteredNavigationFolderCard>>(folders, "StreamAsync").GetAsyncEnumerator(cancelled.Token);
        Func<Task> move = async () => await stream.MoveNextAsync(); await move.Should().ThrowAsync<OperationCanceledException>();
    }
}
