using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.NestedProjectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NestedAliasesCollectionsInitializersAndNullableStructDataAreMaterialized(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        var cards = await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2);
        cards[0].Should().BeEquivalentTo(NestedProjectionCalls.Card(1, " alpha ", 1, false), options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "AllAsync"))[2].Detail.Should().BeNull();
        var arrays = await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "ArraysAsync");
        arrays[0].Should().BeEquivalentTo(NestedProjectionCalls.Array(1, " alpha "), options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "SourceAsync"))
            .Should().BeEquivalentTo(arrays, options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "MaterializedAsync"))[0]
            .Should().BeEquivalentTo(NestedProjectionCalls.Materialized(1, " alpha "), options => options.WithStrictOrdering());
        (await Invoke<Task<IReadOnlyList<NestedProjectionStamp>>>(repository, "StampsAsync"))[0].Should().Be(NestedProjectionCalls.Stamp(1));
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard?>>>(repository, "OptionalAsync")).Should().OnlyContain(row => row == null);
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "NeverAsync"))[0].Detail!.Caption.Should().Be(" alpha ");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NestedResultsRetainPagesGroupsAndFreshDetachedData(bool mapped)
    {
        var (repository, data) = Fixture(mapped); data.CaptureBaseline();
        var page = await Invoke<Task<PagedResult<NestedProjectionCard>>>(repository, "PageAsync", 2, 1);
        page.TotalCount.Should().Be(2); page.Items.Single().Id.Should().Be(2);
        var groups = await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "GroupsAsync");
        groups.Should().HaveCount(2); groups.Single(row => row.Id == 2).Detail!.Value.Should().Be(2);
        var editable = await Invoke<Task<IReadOnlyList<NestedProjectionEditable>>>(repository, "EditableAsync");
        editable[0].Detail = new("edited", 9); editable[0].Items.Clear(); editable[0].Labels[0] = "edited";
        var fresh = (await Invoke<Task<IReadOnlyList<NestedProjectionEditable>>>(repository, "EditableAsync"))[0];
        fresh.Detail!.Caption.Should().Be(" alpha "); fresh.Items.Should().ContainSingle(); fresh.Labels.Should().Equal(" alpha ");
        (await Invoke<Task<NestedProjectionCard?>>(repository, "FindAsync", 999)).Should().BeNull();
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedSaveConflictAndInputOrderedSavedBatchesKeepTheirContracts(bool mapped)
    {
        var (repository, data) = Fixture(mapped); data.CaptureBaseline();
        (await Invoke<Task<CommandResult<NestedProjectionCard>>>(repository, "SavedRenameAsync", new ProjectionNotePatch(1, "bad", 999)))
            .Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<Task<CommandResult>>(repository, "StageRenameAsync", new ProjectionNotePatch(1, " staged ", 1)))
            .Status.Should().Be(CommandStatus.Staged);
        (await Invoke<Task<NestedProjectionCard?>>(repository, "FindAsync", 1))!.Detail!.Caption.Should().Be("ALPHA");
        (await data.SaveChangesAsync()).Should().Be(1);
        (await Invoke<Task<NestedProjectionCard?>>(repository, "FindAsync", 1))!.Detail!.Caption.Should().Be("STAGED");
        IReadOnlyList<ProjectionNotePatch> input = [new(2, " second ", 2), new(1, " first ", 1)];
        var batch = await Invoke<Task<CommandResult<IReadOnlyList<NestedProjectionCard>>>>(repository, "RenameManyAsync", input);
        batch.Value!.Select(row => row.Id).Should().Equal(2, 1);
        batch.Value!.Select(row => row.Detail!.Caption).Should().Equal("SECOND", "FIRST");
        data.RestoreBaseline();
        (await Invoke<Task<NestedProjectionCard?>>(repository, "FindAsync", 1))!.Detail!.Caption.Should().Be("ALPHA");
    }

    [Fact]
    public async Task NullableStructRootsAndCollectionElementsRetainTheirValueTypes()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = NestedProjectionTestHarness.Builder();
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionStamp?>("NullableStamp",
            id => ProjectionNoteCalls.Echo<NestedProjectionStamp?>(id),
            id => id < 0 ? null : new NestedProjectionStamp(id, new NestedProjectionDetail("x", id)), allowNestedData: true);
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionBox<IReadOnlyList<NestedProjectionStamp?>>>("NullableElements",
            id => ProjectionNoteCalls.Echo<NestedProjectionBox<IReadOnlyList<NestedProjectionStamp?>>>(id),
            id => new NestedProjectionBox<IReadOnlyList<NestedProjectionStamp?>>(id,
                new NestedProjectionStamp?[] { null, new NestedProjectionStamp(id, new NestedProjectionDetail("x", id)) }), allowNestedData: true);
        builder.Entity<MemoryWriteNote>().HasQuery("NullableStamp").OrderBy(row => row.Id).Select(row => ProjectionNoteCalls.Echo<NestedProjectionStamp?>(-row.Id)).List();
        builder.Entity<MemoryWriteNote>().HasQuery("NullableElements").OrderBy(row => row.Id)
            .Select(row => ProjectionNoteCalls.Echo<NestedProjectionBox<IReadOnlyList<NestedProjectionStamp?>>>(row.Id)).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, _) = Fixture(assembly: assembly);
        (await Invoke<Task<IReadOnlyList<NestedProjectionStamp?>>>(repository, "NullableStampAsync")).Should().OnlyContain(value => value == null);
        var rows = await Invoke<Task<IReadOnlyList<NestedProjectionBox<IReadOnlyList<NestedProjectionStamp?>>>>>(repository, "NullableElementsAsync");
        rows[0].Value[0].Should().BeNull(); rows[0].Value[1].Should().Be(new NestedProjectionStamp(1, new("x", 1)));
    }

    [Fact]
    public async Task EnumerableContractsRequireMaterializationButCanUseStandardListConstructors()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = NestedProjectionTestHarness.Builder();
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionBox<IEnumerable<NestedProjectionDetail>>>("EnumerableData",
            id => ProjectionNoteCalls.Echo<NestedProjectionBox<IEnumerable<NestedProjectionDetail>>>(id),
            id => new NestedProjectionBox<IEnumerable<NestedProjectionDetail>>(id,
                new List<NestedProjectionDetail>(Enumerable.Range(0, 2).Select(value => new NestedProjectionDetail("x", id + value)))), allowNestedData: true);
        builder.Entity<MemoryWriteNote>().HasQuery("EnumerableData").OrderBy(row => row.Id)
            .Select(row => ProjectionNoteCalls.Echo<NestedProjectionBox<IEnumerable<NestedProjectionDetail>>>(row.Id)).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, _) = Fixture(assembly: assembly);
        var result = await Invoke<Task<IReadOnlyList<NestedProjectionBox<IEnumerable<NestedProjectionDetail>>>>>(repository, "EnumerableDataAsync");
        result[0].Value.Should().BeOfType<List<NestedProjectionDetail>>(); result[0].Value.Select(row => row.Value).Should().Equal(1, 2);
    }

    [Fact]
    public async Task StreamsRemainColdSnapshotBasedCancelableAndScriptOverridable()
    {
        var (repository, data) = Fixture();
        var stream = Invoke<IAsyncEnumerable<NestedProjectionCard>>(repository, "StreamAsync");
        ProjectionTranslationTestHarness.Store<TranslatedWriteSeed>(data).Seed(new(1, " before ", 1, false), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Detail!.Caption.Should().Be("BEFORE");
        ProjectionTranslationTestHarness.Store<TranslatedWriteSeed>(data).Seed(new(2, "after", 2, false), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Detail!.Caption.Should().Be("BETA");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await using var canceled = stream.GetAsyncEnumerator(cancellation.Token);
        Func<Task> move = async () => await canceled.MoveNextAsync(); await move.Should().ThrowAsync<OperationCanceledException>();
        IReadOnlyList<NestedProjectionCard> scripted = [NestedProjectionCalls.Array(40, "scripted")];
        ScriptedTestDoubleTestHarness.Returns(repository, "OnCardsAsync", scripted);
        (await Invoke<Task<IReadOnlyList<NestedProjectionCard>>>(repository, "CardsAsync")).Should().BeSameAs(scripted);
    }
}
