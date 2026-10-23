using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadRepositoryDesigner(bool navigations = true, bool enabled = true, bool strict = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        var builder = modelBuilder;
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Navigation.Contracts")
            .UseGeneratedNamespace("Generated.Navigation.Persistence").UseScriptedTestDoubles("Generated.Navigation.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(enabled && strict).GenerateOnlyConfiguredEntities());
        Stores(builder, navigations);
        Queries(builder);
    }

    internal static void Queries(RepositoryModelBuilder builder)
    {
        var library = builder.Entity<NavigationReadLibrary>();
        library.HasVisibilityPolicy("Libraries", row => !row.Hidden);
        library.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new NavigationReadCard(row.Id, row.Name,
            row.Profile == null ? null : row.Profile.Caption, row.Books.Count,
            row.Books.Sum(book => book.Price), row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList())).List();
        library.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => row.Books.Count).SingleOrDefault();
        library.HasQuery("Search").Where<int>((row, minimum) => row.Books.Any(book => book.Price >= minimum)).Select(row => row.Id).List();
        library.HasQuery("Detail").Where<int>((row, id) => row.Id == id).Load(load => load.Include(row => row.Books).ThenInclude(book => book.Author))
            .Select(row => new NavigationReadDetail(row.Id, row.Books.OrderBy(book => book.Id)
                .Select(book => new NavigationReadBookView { Id = book.Id, AuthorName = book.Author.Name }).ToList())).SingleOrDefault();
        library.HasQuery("Nested").OrderBy(row => row.Id).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).Paged();
        library.HasQuery("Stream").OrderBy(row => row.Id).Select(row => row.Books.OrderBy(book => book.Id).Select(book => book.Author.Name).ToList()).Stream();
        var book = builder.Entity<NavigationReadBook>();
        book.HasVisibilityPolicy("Books", row => !row.Archived);
        book.HasQuery("References").OrderBy(row => row.Id).Select(row => row.Library == null ? "none" : row.Library.Name).List();
        book.HasQuery("Source").From(sources => sources.Set<NavigationReadBook>().OrderBy(row => row.Id)
            .Select(row => row.Library == null ? "none" : row.Library.Name)).List();
        book.HasQuery("Parents").OrderBy(row => row.Id).Select(row => row.Parent == null ? -1 : row.Parent.Id).List();
    }

    internal static void Stores(RepositoryModelBuilder builder, bool navigations = true)
    {
        var library = builder.Entity<NavigationReadLibrary>();
        library.WriteEnabled = false;
        library.HasInMemoryStore(row => row.Id, row => new NavigationReadLibrary
            { Id = row.Id, Code = row.Code, Region = row.Region, Name = row.Name, Hidden = row.Hidden });
        var book = builder.Entity<NavigationReadBook>();
        book.WriteEnabled = false;
        book.HasInMemoryStore(row => row.Id, row => new NavigationReadBook
            { Id = row.Id, LibraryCode = row.LibraryCode, LibraryRegion = row.LibraryRegion, AuthorId = row.AuthorId, Price = row.Price, Archived = row.Archived, ParentId = row.ParentId });
        var author = builder.Entity<NavigationReadAuthor>();
        author.WriteEnabled = false;
        author.HasInMemoryStore(row => row.Id, row => new NavigationReadAuthor { Id = row.Id, Name = row.Name });
        var profile = builder.Entity<NavigationReadProfile>();
        profile.WriteEnabled = false;
        profile.HasInMemoryStore(row => row.Id, row => new NavigationReadProfile { Id = row.Id, LibraryId = row.LibraryId, Caption = row.Caption });
        if (!navigations) return;
        library.HasInMemoryNavigation(row => row.Books).HasInMemoryNavigation(row => row.Profile);
        book.HasInMemoryNavigation(row => row.Library).HasInMemoryNavigation(row => row.Author).HasInMemoryNavigation(row => row.Parent);
    }
}
