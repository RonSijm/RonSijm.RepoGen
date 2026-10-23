using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MappedReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadExecutionTests
{
    [Fact]
    public async Task ModelScalarsAreReadWithoutEntityGettersAcrossFiltersProjectionsReportsAndGraphs()
    {
        MappedReadItem.GetterCalls.Value = 0;
        var (items, folders, data) = Fixture();
        var cards = await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 5);
        cards[0].Should().BeEquivalentTo(new MappedReadCard(1, "one", "alpha", 20, 2, MappedReadState.Draft, [1, 2], "a", 17));
        cards[1].State.Should().BeNull();
        (await Invoke<Task<IReadOnlyList<string>>>(items, "NamesAsync")).Should().Equal("one", "two", "other folder");
        (await Invoke<Task<IReadOnlyList<string>>>(items, "TagsAsync")).Should().Equal("a", "a", "c");
        (await Invoke<Task<IReadOnlyList<int>>>(items, "ByRankAsync", 20)).Should().Equal(1, 5);
        (await Invoke<Task<int>>(items, "CountAsync")).Should().Be(3);
        (await Invoke<Task<IReadOnlyList<MappedReadTotal>>>(items, "TotalsAsync")).Should().Equal(new MappedReadTotal("a", 2, 30), new MappedReadTotal("c", 1, 30));
        (await Invoke<Task<IReadOnlyList<string>>>(items, "FolderNamesAsync")).Should().Equal("open", "open", "none");
        (await Invoke<Task<IReadOnlyList<List<string>>>>(folders, "CaptionsAsync")).Single().Should().Equal("alpha", "beta", "hidden");
        MappedReadItem.GetterCalls.Value.Should().Be(0);
        ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem)).Count.Should().Be(5);
    }

    [Fact]
    public async Task DynamicWhitelistedSortsUseBoundReadSlotsAndDoNotInvokeGetters()
    {
        var (items, _, _) = Fixture();
        (await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "SortedAsync", "Name", false)).Select(row => row.Id).Should().Equal(1, 5, 2);
        (await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "SortedAsync", "Caption", true)).Select(row => row.Id).Should().Equal(5, 2, 1);
        Func<Task> invalid = async () => await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "SortedAsync", "Name; DROP TABLE", false);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task BaselinesAndColdStreamsRetainDetachedBinaryAndScalarValues()
    {
        var (items, _, data) = Fixture();
        var store = ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem));
        data.CaptureBaseline();
        (await Invoke<Task<MappedReadCard?>>(items, "GetAsync", 1))!.Fingerprint![0] = 99;
        (await Invoke<Task<MappedReadCard?>>(items, "GetAsync", 1))!.Fingerprint.Should().Equal(1, 2);
        var page = await Invoke<Task<PagedResult<MappedReadCard>>>(items, "PageAsync", 2, 2);
        page.TotalCount.Should().Be(3);
        page.Items.Single().Id.Should().Be(5);
        await using (var enumerator = Invoke<IAsyncEnumerable<MappedReadCard>>(items, "StreamAsync").GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            store.Seed(MappedReadTestHarness.Seeds()[1] with { Caption = "changed", Archived = true }, InMemoryDuplicateKeyBehavior.Replace);
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Caption.Should().Be("beta");
        }
        (await Invoke<Task<MappedReadCard?>>(items, "GetAsync", 2)).Should().BeNull();
        data.RestoreBaseline();
        (await Invoke<Task<MappedReadCard?>>(items, "GetAsync", 2))!.Caption.Should().Be("beta");
        (await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "AllAsync")).Should().HaveCount(4);
        (await Invoke<Task<IReadOnlyList<MappedReadCard>>>(items, "IncludingHiddenAsync")).Should().HaveCount(4);
    }

    [Fact]
    public async Task UnboundReadsStillFailOnEmptyStoresAndScriptsRetainPrecedence()
    {
        using var context = MappedReadTestHarness.Context();
        var builder = Base(strict: false);
        builder.Entity<MappedReadItem>().WithoutInMemoryProperty("audit").HasQuery("Read").Select(row => EF.Property<int>(row, "audit")).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (items, _, data) = Fixture(assembly);
        ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem)).Clear();
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(items, "ReadAsync");
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(items, "OnReadAsync", (IReadOnlyList<int>)[99]);
        (await Invoke<Task<IReadOnlyList<int>>>(items, "ReadAsync")).Should().Equal(99);
    }

    [Fact]
    public async Task ExplicitReadKeyValuesDoNotRedefineFixtureStoreIdentity()
    {
        using var context = MappedReadTestHarness.Context();
        var builder = Base();
        builder.Entity<MappedReadItem>().MapInMemoryEntity<MappedReadSeed>(row => new MappedReadItem { FolderId = row.FolderId })
            .MapInMemoryProperty<MappedReadSeed, int>(nameof(MappedReadItem.Id), row => row.Id + 100)
            .HasQuery("Read").OrderBy(row => row.Id).Select(row => row.Id).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (items, _, data) = Fixture(assembly);
        (await Invoke<Task<IReadOnlyList<int>>>(items, "ReadAsync")).Should().Equal(101, 102, 105);
        ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem)).Find(1).Should().NotBeNull();
        ModelFilterTestHarness.Store<MappedReadSeed>(data, nameof(MappedReadItem)).Find(101).Should().BeNull();
    }
}
