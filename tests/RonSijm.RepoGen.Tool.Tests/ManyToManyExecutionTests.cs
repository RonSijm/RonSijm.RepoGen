using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyToManyExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompositeAlternateKeysAndExplicitInversesComeOnlyFromStores(bool mapped)
    {
        var (owner, tag, _) = Repositories(mapped);
        var cards = await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync");
        cards.Should().BeEquivalentTo(new[] { new ManyReadCard(1, "North", ["Red", "Blue"]), new ManyReadCard(2, "South", ["Blue"]), new ManyReadCard(4, "Empty", []) }, options => options.WithStrictOrdering());
        var inverse = await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "OwnersAsync");
        inverse.Should().BeEquivalentTo(new[] { new ManyReadTagCard(1, "Red", ["North", "Hidden"]), new ManyReadTagCard(3, "Unused", []) }, options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SearchAsync", "Blue")).Should().Equal(1, 2);
        (await Invoke<Task<IReadOnlyList<int>>>(owner, "SourceAsync")).Should().Equal(2, 1, 0);
        (await Invoke<Task<IReadOnlyList<List<int>>>>(owner, "CyclesAsync")).Should().BeEquivalentTo(new[] { new List<int> { 2, 2 }, new List<int> { 2 }, new List<int>() }, options => options.WithStrictOrdering());
        var explicitFilter = await Invoke<Task<IReadOnlyList<List<string>>>>(owner, "VisibleTagsAsync");
        explicitFilter.Should().BeEquivalentTo(new[] { new List<string> { "Red" }, new List<string>(), new List<string>() }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ReturnedListsNeverChangeStoresFutureCallsOrBaselineAndScriptsOverrideLoading()
    {
        var (repository, _, data) = Repositories(); data.CaptureBaseline();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync"))[0].Tags.Clear();
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
        LinkStore(data).Remove(2); (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync"))[0].Tags.Should().Equal("Blue");
        data.RestoreBaseline(); LinkStore(data).Count.Should().Be(4);
        TagStore(data).Remove(1);
        ScriptedTestDoubleTestHarness.Returns(repository, "OnCardsAsync", (IReadOnlyList<ManyReadCard>)[new(99, "scripted", [])]);
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync")).Single().Id.Should().Be(99);
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        Func<Task> missing = async () => await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync");
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*required join reference*target*");
    }

    [Fact]
    public async Task PagesAndColdStreamsUseOneDetachedParticipatingTableSnapshot()
    {
        var (repository, _, data) = Repositories();
        var page = await Invoke<Task<PagedResult<List<string>>>>(repository, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().Should().Equal("Blue");
        var stream = Invoke<IAsyncEnumerable<List<string>>>(repository, "StreamAsync");
        TagStore(data).Seed(new(2, "before", true), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("Red", "before");
        TagStore(data).Seed(new(2, "after", true), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("before");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("pair")]
    public async Task InvalidJoinRowsFailEvenWhenTheRootPredicateWouldExcludeTheirOwner(string scenario)
    {
        var (repository, _, data) = Repositories();
        LinkStore(data).Seed(scenario == "owner" ? new(99, 99, "missing", 1, true) : new(99, 1, "hidden", 1, true));
        Func<Task> load = async () => await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repository, "CardsAsync");
        await load.Should().ThrowAsync<InvalidOperationException>().WithMessage(scenario == "owner" ? "*Tags*required join reference*owner*" : "*Tags*join relationship pairs*");
        OwnerStore(data).Snapshot().Single(row => row.Id == 1).Name.Should().Be("North"); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task SingleAndBatchSavedResultsReuseFreshJoinSnapshotsWithoutGraphMutation()
    {
        var (repository, _, data) = Repositories();
        var single = await Invoke<Task<CommandResult<ManyReadCard>>>(repository, "RenameAsync", 1);
        single.Value!.Should().BeEquivalentTo(new ManyReadCard(1, "renamed", ["Red", "Blue"]));
        IReadOnlyList<ManyReadPatch> inputs = [new(2, "second"), new(1, "first")];
        var batch = await Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(repository, "RenameManyAsync", inputs);
        var values = batch.Value!;
        values.Select(row => row.Id).Should().Equal(2, 1); values[0].Tags.Should().Equal("Blue"); values[1].Tags.Should().Equal("Red", "Blue");
        OwnerStore(data).Find((1, 1))!.Name.Should().Be("first"); LinkStore(data).Count.Should().Be(4); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidPostSaveJoinGraphDoesNotUndoTheSavedScalarWrite()
    {
        var (repository, _, data) = Repositories(); LinkStore(data).Seed(new(99, 99, "missing", 1, false));
        Func<Task> save = async () => await Invoke<Task<CommandResult<ManyReadCard>>>(repository, "RenameAsync", 1);
        await save.Should().ThrowAsync<RepositorySavedResultException>();
        OwnerStore(data).Find((1, 1))!.Name.Should().Be("renamed"); data.PendingCount.Should().Be(0);
    }
}
