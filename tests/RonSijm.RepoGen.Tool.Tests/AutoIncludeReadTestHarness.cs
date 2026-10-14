using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class AutoIncludeReadTestHarness
{
    internal const string Namespace = "Generated.AutoIncludes";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static AutoIncludeReadContext Context() => new(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool ignore = true, bool strict = true, bool backend = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(backend).RequirePortableTestQueries(strict && backend));
        if (mapped) ProxyNavigationRepositoryDesigner.Stores(builder);
        else NavigationReadRepositoryDesigner.Stores(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        library.HasVisibilityPolicy("Libraries", row => !row.Hidden);
        library.HasLoadProfile("Details", load => load.Include(row => row.Books).ThenInclude(book => book.Author));
        library.HasProjection<NavigationReadDetail>(row => new NavigationReadDetail(row.Id, row.Books.OrderBy(book => book.Id)
            .Select(book => new NavigationReadBookView { Id = book.Id, AuthorName = book.Author.Name }).ToList()));
        library.HasQuery("Cards").IgnoreAutoIncludes(ignore).OrderBy(row => row.Id).Select(row => new AutoIncludeReadCard(row.Id, row.Name)).List();
        library.HasQuery("Detail").IgnoreAutoIncludes(ignore).Where<int>((row, id) => row.Id == id).UseLoadProfile("Details")
            .ProjectTo<NavigationReadDetail>().SingleOrDefault();
        library.HasQuery("Page").OrderBy(row => row.Id).Select(row => new AutoIncludeReadCard(row.Id, row.Name)).IgnoreAutoIncludes(ignore).Paged();
        library.HasQuery("Counts").GroupBy(row => row.Hidden, group => group.Count()).IgnoreAutoIncludes(ignore).List();
        library.HasQuery("Stream").OrderBy(row => row.Id).Select(row => new AutoIncludeReadCard(row.Id, row.Name)).IgnoreAutoIncludes(ignore).Stream();
        library.HasQuery("Joined").From(sources => sources.Set<NavigationReadBook>().Join(sources.Set<NavigationReadAuthor>(),
                book => book.AuthorId, author => author.Id, (book, author) => new { book.Id, author.Name })
                .OrderBy(row => row.Id).Select(row => new AutoIncludeReadCard(row.Id, row.Name)))
            .IgnoreAutoIncludes(ignore).List();
        library.HasQuery("ProjectedAuthors").From(sources => sources.Set<NavigationReadLibrary>().OrderBy(row => row.Id))
            .IgnoreAutoIncludes(ignore).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).List();
        var book = builder.Entity<NavigationReadBook>();
        book.HasVisibilityPolicy("Books", row => !row.Archived);
        book.HasQuery("References").IgnoreAutoIncludes(ignore).OrderBy(row => row.Id).Select(row => row.Library == null ? "none" : row.Library.Name).List();
        return builder;
    }

    internal static (object Library, object Book, InMemoryDataContext Data) Fixture(bool mapped)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.AutoIncludeReadContextRepositoryFixture")!)!;
        var library = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.NavigationReadLibraryRepositoryTestDouble")!, fixture)!;
        var book = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.NavigationReadBookRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) ProxyNavigationTestHarness.Seed(data); else NavigationReadTestHarness.Seed(data);
        return (library, book, data);
    }
}
