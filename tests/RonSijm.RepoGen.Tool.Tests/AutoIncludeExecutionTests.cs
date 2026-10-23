using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AutoIncludeReadTestHarness;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedReadsRetainVisibilityProjectionExplicitLoadingAndAllRootPolicies(bool mapped)
    {
        var (library, book, _) = Fixture(mapped);
        (await Invoke<Task<IReadOnlyList<AutoIncludeReadCard>>>(library, "CardsAsync")).Select(row => row.Id).Should().Equal(1, 2, 4);
        (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!.Books.Select(row => row.AuthorName).Should().Equal("Ada", "Bob");
        (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 3)).Should().BeNull();
        (await Invoke<Task<IReadOnlyList<AutoIncludeReadCard>>>(library, "JoinedAsync")).Select(row => row.Id).Should().Equal(1, 3, 4, 5);
        (await Invoke<Task<IReadOnlyList<List<string>>>>(library, "ProjectedAuthorsAsync"))[0].Should().Equal("Ada", "Bob");
        (await Invoke<Task<IReadOnlyList<string>>>(book, "ReferencesAsync")).Should().Equal("North", "South", "Hidden", "none");
        var page = await Invoke<Task<PagedResult<AutoIncludeReadCard>>>(library, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<int>>>(library, "CountsAsync")).Should().Equal(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetachedGraphsAndColdStreamsRetainSnapshotAndBaselineIsolation(bool mapped)
    {
        var (library, _, data) = Fixture(mapped); data.CaptureBaseline();
        var first = (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!;
        first.Books[0].AuthorName = "caller"; first.Books.Clear();
        (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!.Books.Select(row => row.AuthorName).Should().Equal("Ada", "Bob");
        var cold = Invoke<IAsyncEnumerable<AutoIncludeReadCard>>(library, "StreamAsync");
        if (mapped) ProxyNavigationTestHarness.Store<ProxyNavigationLibraryData>(data, nameof(NavigationReadLibrary))
            .Seed(new(2, "same", "south", "before enumeration"), InMemoryDuplicateKeyBehavior.Replace);
        else Store<NavigationReadLibrary>(data).Seed(new() { Id = 2, Code = "same", Region = "south", Name = "before enumeration" }, InMemoryDuplicateKeyBehavior.Replace);
        await using var stream = cold.GetAsyncEnumerator(); (await stream.MoveNextAsync()).Should().BeTrue();
        data.Clear(); (await stream.MoveNextAsync()).Should().BeTrue(); stream.Current.Name.Should().Be("before enumeration");
        (await Invoke<Task<IReadOnlyList<AutoIncludeReadCard>>>(library, "CardsAsync")).Should().BeEmpty();
        data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<AutoIncludeReadCard>>>(library, "CardsAsync"))[1].Name.Should().Be("South");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAndPagingValidationRemainIntact(bool mapped)
    {
        var (library, _, _) = Fixture(mapped);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> read = async () => await (Task<IReadOnlyList<AutoIncludeReadCard>>)library.GetType().GetMethod("CardsAsync")!.Invoke(library, [cancellation.Token])!;
        await read.Should().ThrowAsync<OperationCanceledException>();
        await using var stream = Invoke<IAsyncEnumerable<AutoIncludeReadCard>>(library, "StreamAsync").GetAsyncEnumerator(cancellation.Token);
        Func<Task> move = async () => await stream.MoveNextAsync(); await move.Should().ThrowAsync<OperationCanceledException>();
        Func<Task> page = async () => await Invoke<Task<PagedResult<AutoIncludeReadCard>>>(library, "PageAsync", 0, 1);
        await page.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
