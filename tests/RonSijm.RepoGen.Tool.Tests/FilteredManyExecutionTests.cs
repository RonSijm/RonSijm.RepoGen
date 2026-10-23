using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredManyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequiredFiltersOmitLinksNotOwnersAndRelatedDesignerVisibilityIsNotInjected(bool mapped)
    {
        var (owner, tag, data) = Fixture(mapped); data.CaptureBaseline();
        var cards = await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 4);
        cards[0].Tags.Should().Equal("Red", "Private tag"); cards[1].Tags.Should().BeEmpty(); cards[2].Tags.Should().BeEmpty();
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "RootBypassAsync")).Should().Equal(1, 2, 4, 5);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SearchAsync", "Archived tag")).Should().BeEmpty();
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SearchAsync", "Private tag")).Should().Equal(1);
        var inverse = await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "OwnersAsync");
        inverse.Select(row => row.Id).Should().Equal(1, 3); inverse[0].Owners.Should().Equal("North", "Private owner");
        var all = await Invoke<Task<IReadOnlyList<List<string>>>>(owner, "AllAsync");
        all.Should().HaveCount(5); all[0].Should().Equal("Red", "Archived tag", "Private tag");
        if (mapped) { OwnerStore(data).Count.Should().Be(5); TagStore(data).Count.Should().Be(4); LinkStore(data).Count.Should().Be(6); }
        cards[0].Tags.Clear();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().HaveCount(2);
        data.Clear(); data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync")).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalTargetsRetainNullSlotsOnlyThroughTheExistingExplicitPolicy(bool mapped)
    {
        var (owner, _, _) = Fixture(mapped, optional: true);
        var cards = await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 4); cards.Select(row => row.Total).Should().Equal(2, 1, 0);
        cards.Select(row => row.Missing).Should().Equal(0, 1, 0); cards[1].Tags.Should().Equal((string?)null);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "HasMissingAsync")).Should().Equal(2);
        var saved = await Invoke<Task<CommandResult<NullTargetReadCard>>>(owner, "RenameAsync", 2);
        saved.Value!.Missing.Should().Be(1); saved.Value.Total.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedResultsUseFreshFilteredSnapshotsAndPreserveBatchInputOrder(bool mapped)
    {
        var (owner, _, data) = Fixture(mapped);
        var renamed = await Invoke<Task<CommandResult<ManyReadCard>>>(owner, "RenameAsync", 1);
        renamed.Value!.Tags.Should().Equal("Red", "Private tag");
        IReadOnlyList<ManyReadPatch> batch = [new(2, "second"), new(1, "first")];
        var result = await Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(owner, "RenameManyAsync", batch);
        var values = result.Value!;
        values.Select(row => row.Id).Should().Equal(2, 1); values[0].Tags.Should().BeEmpty();
        var transaction = await data.BeginTransactionAsync();
        await Invoke<Task<CommandResult<ManyReadCard>>>(owner, "RenameAsync", 1);
        await transaction.RollbackAsync(); await transaction.DisposeAsync();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Name.Should().Be("first");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ColdStreamsCaptureFilteredEndpointsAndJoinsOncePerEnumeration(bool mapped)
    {
        var (owner, _, data) = Fixture(mapped);
        var stream = Invoke<IAsyncEnumerable<List<string>>>(owner, "StreamAsync");
        await using var enumerator = stream.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Equal("Red", "Private tag");
        if (mapped) TagStore(data).ReplaceWith([]); else Store<ManyReadTag, int>(data, nameof(ManyReadTag)).Clear();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().BeEmpty();
        // Truly absent required rows are not equivalent to filtered rows.
        Func<Task> fresh = async () => await Invoke<Task<PagedResult<List<string>>>>(owner, "PageAsync", 2, 1);
        await fresh.Should().ThrowAsync<InvalidOperationException>().WithMessage("*required join reference*target*");
    }
}
