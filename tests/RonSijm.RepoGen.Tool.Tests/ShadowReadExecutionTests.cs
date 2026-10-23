using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ShadowReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadExecutionTests
{
    [Fact]
    public async Task NamedQueriesUseExplicitShadowValuesAcrossAllScalarPipelines()
    {
        var (notes, folders, data) = Fixture();
        var cards = await Invoke<Task<IReadOnlyList<ShadowReadCard>>>(notes, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(2, 1, 5);
        cards[0].Should().BeEquivalentTo(new ShadowReadCard(2, "two", 10, "a", 7, ShadowReadStatus.Published, null));
        (await Invoke<Task<IReadOnlyList<int>>>(notes, "RankedAsync", 20)).Should().Equal(1, 5);
        (await Invoke<Task<int>>(notes, "CountAsync")).Should().Be(3);
        (await Invoke<Task<bool>>(notes, "ExistsAsync", "b")).Should().BeFalse();
        (await Invoke<Task<int>>(notes, "MaximumAsync")).Should().Be(30);
        (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 3)).Should().BeNull();
        (await Invoke<Task<ShadowReadCard?>>(notes, "FirstAsync"))!.Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<ShadowReadGroup>>>(notes, "GroupsAsync")).Should().Equal(new ShadowReadGroup("a", 2, 30), new ShadowReadGroup("c", 1, 30));
        (await Invoke<Task<IReadOnlyDictionary<int, int>>>(notes, "LookupAsync")).Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 20, [2] = 10, [5] = 30 });
        var page = await Invoke<Task<PagedResult<ShadowReadCard>>>(notes, "PageAsync", 2, 2);
        page.TotalCount.Should().Be(3);
        page.Items.Single().Id.Should().Be(5);
        (await Invoke<Task<IReadOnlyList<string>>>(notes, "FoldersAsync")).Should().Equal("open", "open", "none");
        (await Invoke<Task<IReadOnlyList<bool?>>>(notes, "ArchiveFlagsAsync")).Should().Equal(false, false, null);
        (await Invoke<Task<IReadOnlyList<List<int>>>>(folders, "RanksAsync")).Single().Should().Equal(20, 10, 40);
        NoteStore(data).Count.Should().Be(5);
    }

    [Fact]
    public async Task FilterBypassDoesNotBypassVisibilityOrMutateUnderlyingData()
    {
        var (notes, _, data) = Fixture();
        (await Invoke<Task<IReadOnlyList<ShadowReadCard>>>(notes, "AllAsync")).Select(row => row.Id).Should().Equal(3, 2, 1, 5);
        (await Invoke<Task<IReadOnlyList<ShadowReadCard>>>(notes, "IncludingHiddenAsync")).Select(row => row.Id).Should().Equal(2, 1, 5, 4);
        data.CaptureBaseline();
        var card = (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 1))!;
        card.Fingerprint![0] = 99;
        (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 1))!.Fingerprint.Should().Equal(1, 2);
        NoteStore(data).Seed(ShadowReadTestHarness.Seeds()[0] with { Deleted = true }, InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 1)).Should().BeNull();
        data.RestoreBaseline();
        (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 1))!.Fingerprint.Should().Equal(1, 2);
    }

    [Fact]
    public async Task ColdStreamsRetainOneDetachedSnapshotAndCancellationAndScriptsRemainIntact()
    {
        var (notes, _, data) = Fixture();
        var stream = Invoke<IAsyncEnumerable<ShadowReadCard>>(notes, "StreamAsync");
        await using (var enumerator = stream.GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Id.Should().Be(2);
            NoteStore(data).Seed(ShadowReadTestHarness.Seeds()[0] with { Rank = 99 }, InMemoryDuplicateKeyBehavior.Replace);
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Rank.Should().Be(20);
        }
        (await Invoke<Task<ShadowReadCard?>>(notes, "GetAsync", 1))!.Rank.Should().Be(99);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using (var enumerator = stream.GetAsyncEnumerator(cancelled.Token))
        {
            Func<Task> move = async () => await enumerator.MoveNextAsync();
            await move.Should().ThrowAsync<OperationCanceledException>();
        }
        ScriptedTestDoubleTestHarness.Returns(notes, "OnCardsAsync", (IReadOnlyList<ShadowReadCard>)[]);
        (await Invoke<Task<IReadOnlyList<ShadowReadCard>>>(notes, "CardsAsync")).Should().BeEmpty();
        notes.GetType().GetMethod("ResetTestDouble")!.Invoke(notes, null);
        (await Invoke<Task<IReadOnlyList<ShadowReadCard>>>(notes, "CardsAsync")).Should().HaveCount(3);
    }

    [Fact]
    public async Task UnboundNonStrictOperationsFailOnEmptyStoresInsteadOfReportingEmptySuccess()
    {
        using var context = ShadowReadTestHarness.Context();
        var builder = Base(strict: false);
        builder.Entity<ShadowReadNote>().WithoutInMemoryProperty("Rank").HasQuery("Read")
            .Select(row => Microsoft.EntityFrameworkCore.EF.Property<int>(row, "Rank")).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (notes, _, data) = Fixture(assembly);
        NoteStore(data).Clear();
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(notes, "ReadAsync");
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }
}
