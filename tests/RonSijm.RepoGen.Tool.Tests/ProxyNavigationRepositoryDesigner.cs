using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyNavigationRepositoryDesigner(bool enabled = true, bool declarations = true, bool keyed = false) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        var builder = modelBuilder;
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.ProxyNavigation.Contracts")
            .UseGeneratedNamespace("Generated.ProxyNavigation.Persistence").UseScriptedTestDoubles("Generated.ProxyNavigation.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(enabled && declarations).GenerateOnlyConfiguredEntities());
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        Stores(builder, declarations);
        if (!keyed) NavigationReadRepositoryDesigner.Queries(builder);
        else
        {
            builder.Entity<NavigationReadLibrary>().HasVisibilityPolicy("Libraries", row => !row.Hidden);
            builder.Entity<NavigationReadLibrary>().HasQuery("Partition").UseContextKey<string, string>(key => key)
                .OrderBy(row => row.Id).Select(row => row.Books.Select(book => book.Author.Name).ToList()).List();
        }
    }

    internal static void Stores(RepositoryModelBuilder builder, bool declarations = true)
    {
        NavigationReadRepositoryDesigner.Stores(builder, declarations);
        builder.Entity<NavigationReadLibrary>().HasInMemoryStore<ProxyNavigationLibraryData, int>(row => row.Key,
                row => new ProxyNavigationLibraryData(row.Key, row.Code, row.Region, row.Caption, row.Hidden))
            .MapInMemoryEntity<ProxyNavigationLibraryData>(row => new NavigationReadLibrary { Id = row.Key, Code = row.Code, Region = row.Region, Name = row.Caption, Hidden = row.Hidden });
        builder.Entity<NavigationReadBook>().HasInMemoryStore<ProxyNavigationBookData, int>(row => row.Key,
                row => new ProxyNavigationBookData(row.Key, row.Code, row.Region, row.AuthorKey, row.Price, row.Archived, row.ParentKey))
            .MapInMemoryEntity<ProxyNavigationBookData>(row => new NavigationReadBook
            { Id = row.Key, LibraryCode = row.Code, LibraryRegion = row.Region, AuthorId = row.AuthorKey, Price = row.Price, Archived = row.Archived, ParentId = row.ParentKey });
        builder.Entity<NavigationReadAuthor>().HasInMemoryStore<ProxyNavigationAuthorData, int>(row => row.Key, row => new ProxyNavigationAuthorData(row.Key, row.Caption))
            .MapInMemoryEntity<ProxyNavigationAuthorData>(row => new NavigationReadAuthor { Id = row.Key, Name = row.Caption });
        builder.Entity<NavigationReadProfile>().HasInMemoryStore<ProxyNavigationProfileData, int>(row => row.Key, row => new ProxyNavigationProfileData(row.Key, row.LibraryKey, row.Caption))
            .MapInMemoryEntity<ProxyNavigationProfileData>(row => new NavigationReadProfile { Id = row.Key, LibraryId = row.LibraryKey, Caption = row.Caption });
    }
}
