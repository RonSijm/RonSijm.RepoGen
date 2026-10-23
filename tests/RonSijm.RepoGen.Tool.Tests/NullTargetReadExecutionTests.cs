using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NullTargetReadExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MissingTargetsRemainPerLinkRowsAndNullSafeReadsRespectDirectionAndRootVisibility(bool mapped, bool both)
    {
        var (owner, tag, data) = NullTargetReadTestHarness.Repositories(mapped, both);
        var cards = await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 4); cards[0].Total.Should().Be(4); cards[0].Missing.Should().Be(2);
        cards[0].Tags.Should().Equal(null, null, "Red", "Blue"); cards[1].Tags.Should().Equal("Blue"); cards[2].Tags.Should().BeEmpty();
        (await Invoke<Task<IReadOnlyList<long>>>(owner, "CountsAsync")).Should().Equal(4, 1, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "NamedCountsAsync")).Should().Equal(2, 0, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SourceAsync")).Should().Equal(2, 0, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "HasMissingAsync")).Should().Equal(1);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SearchAsync", "Blue")).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<List<string>>>>(owner, "FilteredAsync"))[0].Should().Equal("Red");
        if (both) (await Invoke<Task<IReadOnlyList<int>>>(tag, "OwnersAsync")).Should().Equal(1, 0);
        else (await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "OwnersAsync"))[0].Owners.Should().Equal("North", "Hidden");
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResultsAndBaselinesAreDetachedAndLinkChangesOnlyAffectFreshReads(bool mapped)
    {
        var (owner, _, data) = NullTargetReadTestHarness.Repositories(mapped); data.CaptureBaseline();
        ((List<string?>)(await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync"))[0].Tags).Clear();
        (await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().HaveCount(4);
        if (mapped) LinkStore(data).Seed(new(2, 1, "same", null, false), InMemoryDuplicateKeyBehavior.Replace);
        else Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Seed(new() { Id = 2, TenantId = 1, OwnerCode = "same", TagId = null }, InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync"))[0].Missing.Should().Be(3);
        data.RestoreBaseline(); (await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync"))[0].Missing.Should().Be(2);
    }

    [Fact]
    public async Task PagesAndColdStreamsUseOneCoordinatedNullableSnapshotPerEnumeration()
    {
        var (owner, _, data) = NullTargetReadTestHarness.Repositories();
        var page = await Invoke<Task<PagedResult<NullTargetReadCard>>>(owner, "PageAsync", 2, 1); page.TotalCount.Should().Be(3); page.Items.Single().Tags.Should().Equal("Blue");
        var stream = Invoke<IAsyncEnumerable<NullTargetReadCard>>(owner, "StreamAsync");
        TagStore(data).Seed(new(2, "before", true), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Tags.Should().Equal(null, null, "Red", "before");
        LinkStore(data).Seed(new(3, 2, "same", null, false), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Tags.Should().Equal("before");
        (await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync"))[1].Tags.Should().Equal((string?)null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedSingleAndBatchResultsPreserveNullSlotsWithoutWritingAnyLinks(bool mapped)
    {
        var (owner, _, data) = NullTargetReadTestHarness.Repositories(mapped);
        (await Invoke<Task<CommandResult<NullTargetReadCard>>>(owner, "RenameAsync", 1)).Value!.Tags.Should().Equal(null, null, "Red", "Blue");
        IReadOnlyList<ManyReadPatch> input = [new(2, "second"), new(1, "first")];
        var batch = await Invoke<Task<CommandResult<IReadOnlyList<NullTargetReadCard>>>>(owner, "RenameManyAsync", input);
        batch.Value!.Select(row => row.Id).Should().Equal(2, 1); batch.Value![1].Missing.Should().Be(2);
        (mapped ? LinkStore(data).Count : Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Count).Should().Be(6);
    }

    [Theory]
    [InlineData("pair")]
    [InlineData("owner")]
    public async Task PreservationDoesNotDisableRequiredOwnerOrCompletePairValidation(string invalid)
    {
        var (owner, _, data) = NullTargetReadTestHarness.Repositories();
        LinkStore(data).Seed(invalid == "pair" ? new(99, 1, "same", 1, false) : new(99, 1, "missing", null, false));
        Func<Task> fail = async () => await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync");
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage(invalid == "pair" ? "*pairs are not unique*" : "*required join reference*owner*");
        data.PendingCount.Should().Be(0);
    }
}
