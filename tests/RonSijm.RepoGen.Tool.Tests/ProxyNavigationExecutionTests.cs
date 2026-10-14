using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ProxyNavigationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyNavigationExecutionTests
{
    [Fact]
    public async Task CompositeKeysNestedRequiredReferencesAndOptionalProfilesMatchDeclaredRows()
    {
        var (library, book, _) = ProxyNavigationTestHarness.Repositories();
        var cards = await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 4);
        cards[0].Should().BeEquivalentTo(new NavigationReadCard(1, "North", "Main", 2, 30, ["Ada", "Bob"]));
        cards[2].Should().BeEquivalentTo(new NavigationReadCard(4, "Empty", null, 0, 0, []));
        (await Invoke<Task<IReadOnlyList<string>>>(book, "ReferencesAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<IReadOnlyList<string>>>(book, "SourceAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<IReadOnlyList<int>>>(book, "ParentsAsync")).Should().Equal(-1, -1, -1, -1); // Archived child is a root policy, not a graph filter.
        (await Invoke<Task<int?>>(library, "FindAsync", 1)).Should().Be(2);
        (await Invoke<Task<IReadOnlyList<int>>>(library, "SearchAsync", 20)).Should().Equal(1, 2);
    }

    [Fact]
    public async Task ResultsAndBaselinesRemainDetachedWhileMappedSelfReferencesReuseOneSnapshot()
    {
        var (library, book, data) = ProxyNavigationTestHarness.Repositories(); data.CaptureBaseline();
        var detail = (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!;
        detail.Books[0].AuthorName = "mutated"; detail.Books.Clear();
        (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!.Books.Should().HaveCount(2);
        Store<ProxyNavigationAuthorData>(data, nameof(NavigationReadAuthor)).Find(1)!.Caption.Should().Be("Ada");
        Store<ProxyNavigationBookData>(data, nameof(NavigationReadBook)).Seed(new(3, "same", "south", 2, 30, ParentKey: 1), InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<int>>>(book, "ParentsAsync")).Should().Equal(-1, 1, -1, -1);
        data.RestoreBaseline();
        (await Invoke<Task<IReadOnlyList<int>>>(book, "ParentsAsync")).Should().Equal(-1, -1, -1, -1);
    }

    [Fact]
    public async Task ColdStreamsUseOneMappedGraphPerEnumerationAndScriptsRetainPrecedence()
    {
        var (library, _, data) = ProxyNavigationTestHarness.Repositories();
        var page = await Invoke<Task<PagedResult<List<string>>>>(library, "NestedAsync", 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().Should().Equal("Bob");
        var stream = Invoke<IAsyncEnumerable<List<string>>>(library, "StreamAsync");
        var authors = Store<ProxyNavigationAuthorData>(data, nameof(NavigationReadAuthor));
        authors.Seed(new(1, "before"), InMemoryDuplicateKeyBehavior.Replace);
        await using (var enumerator = stream.GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("before", "Bob");
            authors.Seed(new(2, "after"), InMemoryDuplicateKeyBehavior.Replace);
            (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Equal("Bob");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await using (var enumerator = stream.GetAsyncEnumerator(cancelled.Token))
        {
            Func<Task> move = async () => await enumerator.MoveNextAsync(); await move.Should().ThrowAsync<OperationCanceledException>();
        }
        ScriptedTestDoubleTestHarness.Returns(library, "OnCardsAsync", (IReadOnlyList<NavigationReadCard>)[]);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync")).Should().BeEmpty();
        library.GetType().GetMethod("ResetTestDouble")!.Invoke(library, null);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync")).Should().HaveCount(3);
    }

    [Fact]
    public async Task InvalidRequiredAndDuplicateMappedKeysFailWithoutChangingArrangementData()
    {
        var (library, _, data) = ProxyNavigationTestHarness.Repositories();
        var authors = Store<ProxyNavigationAuthorData>(data, nameof(NavigationReadAuthor)); authors.Remove(1);
        Func<Task> missing = async () => await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync");
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*required reference*");
        authors.Seed(new(1, "Ada"));
        var libraries = Store<ProxyNavigationLibraryData>(data, nameof(NavigationReadLibrary)); libraries.Seed(new(5, "same", "north", "duplicate"));
        Func<Task> duplicate = async () => await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync");
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*keys are not unique*");
        libraries.Count.Should().Be(5); authors.Count.Should().Be(2);
    }

    [Fact]
    public async Task RelatedMappingAndRootPoliciesStayInsideTheSelectedPartition()
    {
        using var context = Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyNavigationRepositoryDesigner(keyed: true)));
        const string ns = "Generated.ProxyNavigation.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "NavigationReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        var second = (InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        ProxyNavigationTestHarness.Seed(first);
        Store<ProxyNavigationLibraryData>(second, nameof(NavigationReadLibrary)).Seed(new(1, "same", "north", "empty"));
        (await Invoke<Task<IReadOnlyList<List<string>>>>(repository, "PartitionAsync", "A"))[0].Should().Equal("Ada", "Bob");
        (await Invoke<Task<IReadOnlyList<List<string>>>>(repository, "PartitionAsync", "B"))[0].Should().BeEmpty();
    }
}
