using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AutoIncludeReadTestHarness;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllNamedPipelinesAgreeWithActualGeneratedSqliteRepositories(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AutoIncludeReadContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await Seed(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, AutoIncludeReadTestHarness.Builder(mapped).Build());
        var realLibrary = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.NavigationReadLibraryRepositoryOperations")!, context)!;
        var realBook = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.NavigationReadBookRepositoryOperations")!, context)!;
        var (library, book, _) = Fixture(mapped);
        async Task Equal<TResult>(object real, object memory, string name, params object?[] inputs)
        {
            var expected = await Invoke<Task<TResult>>(real, name, inputs); var actual = await Invoke<Task<TResult>>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<IReadOnlyList<AutoIncludeReadCard>>(realLibrary, library, "CardsAsync");
        await Equal<NavigationReadDetail?>(realLibrary, library, "DetailAsync", 1);
        await Equal<NavigationReadDetail?>(realLibrary, library, "DetailAsync", 3);
        await Equal<PagedResult<AutoIncludeReadCard>>(realLibrary, library, "PageAsync", 2, 1);
        await Equal<IReadOnlyList<int>>(realLibrary, library, "CountsAsync");
        await Equal<IReadOnlyList<AutoIncludeReadCard>>(realLibrary, library, "JoinedAsync");
        await Equal<IReadOnlyList<List<string>>>(realLibrary, library, "ProjectedAuthorsAsync");
        await Equal<IReadOnlyList<string>>(realBook, book, "ReferencesAsync");
        var actualStream = new List<AutoIncludeReadCard>(); var expectedStream = new List<AutoIncludeReadCard>();
        await foreach (var row in Invoke<IAsyncEnumerable<AutoIncludeReadCard>>(library, "StreamAsync")) actualStream.Add(row);
        await foreach (var row in Invoke<IAsyncEnumerable<AutoIncludeReadCard>>(realLibrary, "StreamAsync")) expectedStream.Add(row);
        actualStream.Should().Equal(expectedStream); context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RealEntityMaterializationSuppressesOnlyModelLoadingAndRetainsExplicitPaths()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AutoIncludeReadContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await Seed(context);
        var builder = AutoIncludeReadTestHarness.Builder(mapped: false);
        var entity = builder.Entity<NavigationReadLibrary>();
        entity.HasQuery("ModelLoading").RequireTestOverride("Exercise actual EF entity-graph mapping.")
            .OrderBy(row => row.Id).ProjectTo(row => AutoIncludeReadMapping.Detail(row)).List();
        entity.HasQuery("NoModelLoading").IgnoreAutoIncludes().RequireTestOverride("Explicit entity-graph mapper.")
            .OrderBy(row => row.Id).ProjectTo(row => AutoIncludeReadMapping.Detail(row)).List();
        entity.HasQuery("ExplicitLoading").IgnoreAutoIncludes().UseLoadProfile("Details").RequireTestOverride("Explicit entity-graph mapper.")
            .OrderBy(row => row.Id).ProjectTo(row => AutoIncludeReadMapping.Detail(row)).List();
        var assembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.NavigationReadLibraryRepositoryOperations")!, context)!;
        var defaults = await Invoke<Task<IReadOnlyList<NavigationReadDetail>>>(real, "ModelLoadingAsync");
        defaults[0].Books.Select(book => book.AuthorName).Should().Equal("Ada", "Bob");
        var disabled = await Invoke<Task<IReadOnlyList<NavigationReadDetail>>>(real, "NoModelLoadingAsync");
        disabled.Should().OnlyContain(row => row.Books.Count == 0);
        var explicitLoading = await Invoke<Task<IReadOnlyList<NavigationReadDetail>>>(real, "ExplicitLoadingAsync");
        explicitLoading[0].Books.Select(book => book.AuthorName).Should().Equal("Ada", "Bob");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RelatedGlobalFiltersStayActiveInProductionAfterSuppressingAutomaticLoading()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new NavigationAutoIncludeContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await Seed(context);
        await context.Set<NavigationReadAuthor>().Where(author => author.Id == 2).ExecuteUpdateAsync(update => update.SetProperty(author => author.Name, "Hidden"));
        var builder = AutoIncludeReadTestHarness.Builder(strict: false);
        var entity = builder.Entity<NavigationReadLibrary>();
        entity.HasQuery("FilteredNames").IgnoreAutoIncludes().OrderBy(row => row.Id).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).List();
        entity.HasQuery("UnfilteredNames").IgnoreAutoIncludes().IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).List();
        var assembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.NavigationReadLibraryRepositoryOperations")!, context)!;
        (await Invoke<Task<IReadOnlyList<List<string>>>>(real, "FilteredNamesAsync"))[0].Should().Equal("Ada");
        (await Invoke<Task<IReadOnlyList<List<string>>>>(real, "UnfilteredNamesAsync"))[0].Should().Equal("Ada", "Hidden");
    }

    private static async Task Seed(NavigationReadContext context)
    {
        await context.Database.EnsureCreatedAsync();
        var libraries = Libraries(); foreach (var library in libraries) library.Books = [];
        context.AddRange(libraries); context.AddRange(new NavigationReadAuthor { Id = 1, Name = "Ada" }, new NavigationReadAuthor { Id = 2, Name = "Bob" });
        context.AddRange(Books()); context.Add(new NavigationReadProfile { Id = 1, LibraryId = 1, Caption = "Main" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task CompatibilityNamedQueriesAlsoHonorTheDesignTimeLoadingPolicy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AutoIncludeReadContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await Seed(context);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseGeneratedNamespace(Namespace + ".Compatibility"));
        var entity = builder.Entity<NavigationReadLibrary>();
        entity.HasQuery("Unloaded").IgnoreAutoIncludes().AsReadOnly().OrderBy(row => row.Id).List();
        entity.HasQuery("Loaded").IgnoreAutoIncludes().AsReadOnly().Load(load => load.Include(row => row.Books).ThenInclude(book => book.Author))
            .OrderBy(row => row.Id).List();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Compatibility.NavigationReadLibraryRepository")!, context)!;
        var extensions = assembly.GetType(Namespace + ".Compatibility.NavigationReadLibraryConfiguredQueryExtensions")!;
        async Task<IReadOnlyList<NavigationReadLibrary>> Call(string name) => await (Task<IReadOnlyList<NavigationReadLibrary>>)extensions.GetMethod(name)!
            .Invoke(null, [real, CancellationToken.None])!;
        (await Call("UnloadedAsync")).Should().OnlyContain(row => row.Books.Count == 0);
        (await Call("LoadedAsync"))[0].Books.Select(book => book.Author.Name).Should().Equal("Ada", "Bob");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
