using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.RelationshipKeyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipKeyExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrdinaryCompositeEdgesShareOnlyTheirDeclaredComponentPolicy(bool mapped)
    {
        var (library, book, data) = NavigationRepositories(mapped);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync"))[0].Count.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<string>>>(book, "ReferencesAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<IReadOnlyList<string>>>(book, "SourceAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<IReadOnlyList<int>>>(library, "CodesAsync", "SAME")).Should().BeEmpty("query predicates are not rewritten");
        (await Invoke<Task<IReadOnlyList<int>>>(library, "CodesAsync", "same")).Should().Equal(1, 2);
        data.CaptureBaseline();
        if (mapped) ProxyNavigationTestHarness.Store<ProxyNavigationBookData>(data, nameof(NavigationReadBook)).Seed(new(1, "SAME", "NORTH", 1, 10), InMemoryDuplicateKeyBehavior.Replace);
        else NavigationReadTestHarness.Store<NavigationReadBook>(data).Seed(new() { Id = 1, LibraryCode = "SAME", LibraryRegion = "NORTH", AuthorId = 1, Price = 10 }, InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync"))[0].Count.Should().Be(1, "the undeclared Region component remains ordinal");
        data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync"))[0].Count.Should().Be(2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task BothJoinDirectionsAndSavedResultsUseThePrincipalPolicyWithoutChangingLinkState(bool mapped, bool optional)
    {
        var (owner, tag, data) = ManyRepositories(mapped, optional);
        if (optional)
        {
            var cards = await Invoke<Task<IReadOnlyList<NullTargetReadCard>>>(owner, "CardsAsync");
            cards[0].Tags.Should().Equal(null, null, "Red", "Blue"); cards[0].Missing.Should().Be(2);
            (await Invoke<Task<CommandResult<NullTargetReadCard>>>(owner, "RenameAsync", 1)).Value!.Tags.Should().Equal(cards[0].Tags);
        }
        else
        {
            (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
            (await Invoke<Task<CommandResult<ManyReadCard>>>(owner, "RenameAsync", 1)).Value!.Tags.Should().Equal("Red", "Blue");
        }
        (await Invoke<Task<IReadOnlyList<ManyReadTagCard>>>(tag, "OwnersAsync"))[0].Owners.Should().Equal("renamed", "Hidden");
        data.PendingCount.Should().Be(0);
        if (mapped) LinkStore(data).Snapshot()[0].OwnerCode.Should().Be("SAME");
        else Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Snapshot()[0].OwnerCode.Should().Be("SAME");
    }

    [Fact]
    public async Task ComparedReadsRemainDetachedAndColdStreamsFreezeCoordinatedRows()
    {
        var (owner, _, data) = ManyRepositories(); data.CaptureBaseline();
        ((List<string>)(await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags).Clear();
        var stream = Invoke<IAsyncEnumerable<List<string>>>(owner, "StreamAsync");
        TagStore(data).Seed(new(2, "before", true), InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator(); (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Equal("Red", "before");
        TagStore(data).Seed(new(2, "after", true), InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("before");
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[1].Tags.Should().Equal("after");
        data.RestoreBaseline(); (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EquivalentDuplicatePairsFailEvenWhenTheStoreHasDistinctPrimaryKeys(bool mapped)
    {
        var (owner, _, data) = ManyRepositories(mapped);
        if (mapped) LinkStore(data).Seed(new(99, 1, "sAmE", 1, false));
        else Store<ManyReadLink, int>(data, nameof(ManyReadLink)).Seed(new() { Id = 99, TenantId = 1, OwnerCode = "sAmE", TagId = 1 });
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync");
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*join relationship pairs are not unique*");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FactoriesRunOnlyWhenTheComparedEdgeIsActuallyLoaded()
    {
        using var context = NavigationContext(); var builder = NavigationBuilder(comparer: false);
        builder.Entity<NavigationReadLibrary>().HasInMemoryRelationshipComparer(row => row.Code, () => new RelationshipKeyThrowingComparer());
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (library, _, _) = NavigationRepositories(assembly: assembly);
        (await Invoke<Task<IReadOnlyList<int>>>(library, "CodesAsync", "same")).Should().Equal(1, 2);
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync");
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("relationship factory executed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshBatchDtosReuseComparedEdgesInInputOrder(bool optional)
    {
        var (owner, _, data) = ManyRepositories(optional: optional);
        IReadOnlyList<ManyReadPatch> input = [new(2, "second"), new(1, "first")];
        if (optional)
        {
            var result = await Invoke<Task<CommandResult<IReadOnlyList<NullTargetReadCard>>>>(owner, "RenameManyAsync", input);
            result.Value!.Select(row => row.Id).Should().Equal(2, 1); result.Value![1].Tags.Should().Equal(null, null, "Red", "Blue");
        }
        else
        {
            var result = await Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(owner, "RenameManyAsync", input);
            result.Value!.Select(row => row.Id).Should().Equal(2, 1); result.Value![1].Tags.Should().Equal("Red", "Blue");
        }
        data.PendingCount.Should().Be(0); LinkStore(data).Snapshot()[0].OwnerCode.Should().Be("SAME");
    }
}
