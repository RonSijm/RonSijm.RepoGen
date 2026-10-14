using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalJoinExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NullableLinksUseDesignedNonNullFiltersWithCompleteCompositeSourceKeys(bool mapped, bool both)
    {
        var (owner, tag, data) = OptionalJoinTestHarness.Repositories(mapped, both);
        var cards = await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync");
        cards.Should().BeEquivalentTo(new[] { new ManyReadCard(1, "North", ["Red", "Blue"]), new ManyReadCard(2, "South", ["Blue"]), new ManyReadCard(4, "Empty", []) }, options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "CountsAsync")).Should().Equal(2, 1, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "NamedCountsAsync")).Should().Equal(2, 1, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SourceAsync")).Should().Equal(2, 1, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SearchAsync", "Blue")).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<List<string>>>>(owner, "VisibleTagsAsync"))[0].Should().Equal("Red");
        var inverse = await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "OwnersAsync");
        inverse[0].Owners.Should().Equal("North", "Hidden");
        inverse[1].Owners.Should().BeEmpty();
        if (!both) (await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "LoadedOwnersAsync")).Should().BeEquivalentTo(inverse);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullLinksAndDtoMutationsCannotChangeFixtureStoresAndFreshReadsUseAllThreeTables(bool mapped)
    {
        var (owner, _, data) = OptionalJoinTestHarness.Repositories(mapped);
        data.CaptureBaseline();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Clear();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
        if (mapped) LinkStore(data).Seed(new(2, 1, "same", null, false), InMemoryDuplicateKeyBehavior.Replace);
        else Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Seed(new() { Id = 2, TenantId = 1, OwnerCode = "same", TagId = null }, InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Blue");
        data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
    }

    [Fact]
    public async Task UnguardedOperationsFailUnlessExplicitlyScriptedAndScriptsOverrideSafeReads()
    {
        var (owner, _, _) = OptionalJoinTestHarness.Repositories();
        Func<Task> unsupported = async () => await Invoke<Task<IReadOnlyList<int>>>(owner, "RawCountsAsync");
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(owner, "OnRawCountsAsync", (IReadOnlyList<int>)[4, 1, 0]);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "RawCountsAsync")).Should().Equal(4, 1, 0);
        ScriptedTestDoubleTestHarness.Returns(owner, "OnCardsAsync", (IReadOnlyList<ManyReadCard>)[new(99, "script", [])]);
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync")).Single().Id.Should().Be(99);
    }

    [Fact]
    public async Task PageAndColdStreamsDoNotObserveMidInvocationChangesToOptionalLinks()
    {
        var (owner, _, data) = OptionalJoinTestHarness.Repositories();
        var page = await Invoke<Task<PagedResult<List<string>>>>(owner, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().Should().Equal("Blue");
        var stream = Invoke<IAsyncEnumerable<List<string>>>(owner, "StreamAsync");
        TagStore(data).Seed(new(2, "before", true), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("Red", "before");
        LinkStore(data).Seed(new(3, 2, "same", null, false), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("before");
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[1].Tags.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pair")]
    [InlineData("requiredSource")]
    public async Task RequiredReferencesAndCompleteDuplicatePairsStillFailBeforeQueryExecution(string invalid)
    {
        var (owner, _, data) = OptionalJoinTestHarness.Repositories();
        LinkStore(data).Seed(invalid == "pair" ? new(99, 1, "same", 1, false) : new(99, 1, "missing", null, false));
        Func<Task> load = async () => await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync");
        await load.Should().ThrowAsync<InvalidOperationException>().WithMessage(invalid == "pair" ? "*pairs are not unique*" : "*required join reference*owner*");
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndBatchSavedResultsReadNullableJoinsWithoutMutatingTheLinks(bool mapped)
    {
        var (owner, _, data) = OptionalJoinTestHarness.Repositories(mapped);
        var result = await Invoke<Task<CommandResult<ManyReadCard>>>(owner, "RenameAsync", 1);
        result.Value!.Tags.Should().Equal("Red", "Blue");
        IReadOnlyList<ManyReadPatch> input = [new(2, "second"), new(1, "first")];
        var batch = await Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(owner, "RenameManyAsync", input);
        var values = batch.Value!;
        values.Select(row => row.Id).Should().Equal(2, 1);
        values[0].Tags.Should().Equal("Blue"); values[1].Tags.Should().Equal("Red", "Blue");
        (mapped ? LinkStore(data).Count : Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Count).Should().Be(6);
    }

    [Fact]
    public async Task InvalidRequiredReferencesAfterSaveDoNotUndoAnAlreadySavedScalarCommand()
    {
        var (owner, _, data) = OptionalJoinTestHarness.Repositories();
        LinkStore(data).Seed(new(99, 1, "missing", null, false));
        Func<Task> save = async () => await Invoke<Task<CommandResult<ManyReadCard>>>(owner, "RenameAsync", 1);
        await save.Should().ThrowAsync<RepositorySavedResultException>();
        OwnerStore(data).Find((1, 1))!.Name.Should().Be("renamed");
        data.PendingCount.Should().Be(0);
    }
}
