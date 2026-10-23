using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadExecutionTests
{
    [Fact]
    public async Task DeliberatelyCopiedWrongSeedGraphsAreReplacedByTheDeclaredStoreRelationships()
    {
        var builder = Builder(strict: true);
        NavigationReadRepositoryDesigner.Stores(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        library.HasInMemoryStore(row => row.Id, row => new NavigationReadLibrary
        {
            Id = row.Id, Code = row.Code, Region = row.Region,
            Books = row.Books.Select(book => new NavigationReadBook { Id = book.Id, Price = book.Price }).ToList()
        });
        builder.Entity<NavigationReadBook>().HasInMemoryStore(row => row.Id, row => new NavigationReadBook
        {
            Id = row.Id, LibraryCode = row.LibraryCode, LibraryRegion = row.LibraryRegion, AuthorId = row.AuthorId,
            Author = new NavigationReadAuthor { Id = 999, Name = "copied wrong author" }
        });
        library.HasQuery("Authors").Where(row => row.Id == 1).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).Single();
        using var context = Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Seed(data);
        Store<NavigationReadLibrary>(data).Find(1)!.Books.Single().Id.Should().Be(99);
        Store<NavigationReadBook>(data).Find(1)!.Author.Name.Should().Be("copied wrong author");
        (await Invoke<Task<List<string>>>(repository, "AuthorsAsync")).Should().Equal("Ada", "Bob");
        Store<NavigationReadLibrary>(data).Find(1)!.Books.Single().Id.Should().Be(99);
    }

    [Fact]
    public async Task NestedCollectionsReferencesAlternateCompositeKeysAndOptionalProfilesAreLoadedFromStores()
    {
        var (library, book, _) = Repositories();
        var cards = await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(library, "CardsAsync");
        cards.Select(row => row.Id).Should().Equal(1, 2, 4);
        cards[0].Should().BeEquivalentTo(new NavigationReadCard(1, "North", "Main", 2, 30, ["Ada", "Bob"]));
        cards[1].Should().BeEquivalentTo(new NavigationReadCard(2, "South", null, 1, 30, ["Bob"]));
        cards[2].Should().BeEquivalentTo(new NavigationReadCard(4, "Empty", null, 0, 0, []));
        (await Invoke<Task<IReadOnlyList<string>>>(book, "ReferencesAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<IReadOnlyList<string>>>(book, "SourceAsync")).Should().Equal("North", "South", "Hidden", "none");
        (await Invoke<Task<int?>>(library, "FindAsync", 1)).Should().Be(2);
        (await Invoke<Task<IReadOnlyList<int>>>(library, "SearchAsync", 20)).Should().Equal(1, 2);
    }

    [Fact]
    public async Task ReturnedGraphsAreDetachedSharedWithinOneSnapshotAndDoNotChangeBaselineOrFutureCalls()
    {
        var (repository, _, data) = Repositories();
        data.CaptureBaseline();
        var detail = (await Invoke<Task<NavigationReadDetail?>>(repository, "DetailAsync", 1))!;
        detail.Books.Select(row => row.Id).Should().Equal(1, 2);
        detail.Books[0].AuthorName = "changed";
        detail.Books.RemoveAt(1);
        Store<NavigationReadLibrary>(data).Find(1)!.Books.Should().BeEmpty();
        Store<NavigationReadAuthor>(data).Find(1)!.Name.Should().Be("Ada");
        (await Invoke<Task<NavigationReadDetail?>>(repository, "DetailAsync", 1))!.Books.Should().HaveCount(2);
        Store<NavigationReadAuthor>(data).Seed(new() { Id = 1, Name = "updated" }, InMemoryDuplicateKeyBehavior.Replace);
        data.RestoreBaseline();
        (await Invoke<Task<NavigationReadDetail?>>(repository, "DetailAsync", 1))!.Books[0].AuthorName.Should().Be("Ada");
    }

    [Fact]
    public async Task OnlyExplicitlyAccessedInversesAreFixedAndSelfReferencesReuseOneStore()
    {
        var (library, book, data) = Repositories();
        var builder = Builder(strict: true);
        NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("Cycles").OrderBy(row => row.Id)
            .Load(load => load.Include(row => row.Books).ThenInclude(row => row.Library))
            .Select(row => row.Books.Any(book => book.Library == row)).List();
        using var context = Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var fixtureData = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Seed(fixtureData);
        var rows = await Invoke<Task<IReadOnlyList<bool>>>(repository, "CyclesAsync");
        rows.Should().Equal(true, true, true, false);
        var detail = (await Invoke<Task<NavigationReadDetail?>>(library, "DetailAsync", 1))!;
        detail.Books.Should().HaveCount(2);
        Store<NavigationReadBook>(data).Seed(new() { Id = 3, AuthorId = 2, ParentId = 1 }, InMemoryDuplicateKeyBehavior.Replace);
        (await Invoke<Task<IReadOnlyList<int>>>(book, "ParentsAsync")).Should().Equal(-1, 1, -1, -1);
    }

    [Fact]
    public async Task PagesAndColdStreamsKeepOneGraphSnapshotWhileScriptsAndResetStillTakePrecedence()
    {
        var (repository, _, data) = Repositories();
        var page = await Invoke<Task<PagedResult<List<string>>>>(repository, "NestedAsync", 2, 1);
        page.TotalCount.Should().Be(3);
        page.Items.Single().Should().Equal("Bob");
        var stream = Invoke<IAsyncEnumerable<List<string>>>(repository, "StreamAsync");
        Store<NavigationReadAuthor>(data).Seed(new() { Id = 1, Name = "before" }, InMemoryDuplicateKeyBehavior.Replace);
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Equal("before", "Bob");
        Store<NavigationReadAuthor>(data).Seed(new() { Id = 2, Name = "after" }, InMemoryDuplicateKeyBehavior.Replace);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Equal("Bob");
        ScriptedTestDoubleTestHarness.Returns(repository, "OnCardsAsync", (IReadOnlyList<NavigationReadCard>)[]);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(repository, "CardsAsync")).Should().BeEmpty();
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(repository, "CardsAsync")).Should().HaveCount(3);
    }

    [Fact]
    public async Task RequiredMissingReferencesAndDuplicateAlternateKeysAreExplicitFixtureErrors()
    {
        var (repository, _, data) = Repositories();
        Store<NavigationReadAuthor>(data).Remove(1);
        Func<Task> required = async () => await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(repository, "CardsAsync");
        await required.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Author*required reference*");
        Store<NavigationReadAuthor>(data).Seed(new() { Id = 1, Name = "Ada" });
        Store<NavigationReadLibrary>(data).Seed(new() { Id = 5, Code = "same", Region = "north" });
        Func<Task> duplicates = async () => await Invoke<Task<IReadOnlyList<NavigationReadCard>>>(repository, "CardsAsync");
        await duplicates.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Books*keys are not unique*");
    }

    [Fact]
    public async Task KeyedFixturesKeepEveryRelatedStoreInTheSelectedPartition()
    {
        var builder = Builder(strict: true);
        builder.Configure(options => options.UseKeyedContextLeases<int>());
        NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("Counts").UseContextKey<int, int>(partition => partition)
            .Select(row => row.Books.Count).List();
        using var context = Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Navigation.TestDoubles.NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [1])!;
        var second = (InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [2])!;
        Seed(first);
        Store<NavigationReadLibrary>(second).Seed(new() { Id = 1, Code = "same", Region = "north" });
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "CountsAsync", 1)).Should().Equal(2, 1, 1, 0);
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "CountsAsync", 2)).Should().Equal(0);
    }
}
