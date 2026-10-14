using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class RelationshipKeyTestHarness
{
    private static readonly Lazy<Assembly> NavigationMapped = new(() => CompileNavigation(true));
    private static readonly Lazy<Assembly> NavigationEntities = new(() => CompileNavigation(false));
    private static readonly Lazy<Assembly> ManyMapped = new(() => CompileMany(true, false));
    private static readonly Lazy<Assembly> ManyEntities = new(() => CompileMany(false, false));
    private static readonly Lazy<Assembly> OptionalMapped = new(() => CompileMany(true, true));
    private static readonly Lazy<Assembly> OptionalEntities = new(() => CompileMany(false, true));

    internal static RelationshipNavigationContext NavigationContext(DbContextOptions<NavigationReadContext>? options = null)
        => new(options ?? new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);
    internal static ManyReadContext ManyContext(bool optional = false, DbContextOptions<ManyReadContext>? options = null)
        => optional ? new RelationshipManyOptionalContext(options ?? new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options)
            : new RelationshipManyContext(options ?? new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder NavigationBuilder(bool mapped = true, bool comparer = true, bool queries = true, bool strict = true)
    {
        var builder = NavigationReadTestHarness.Builder(strict && queries);
        builder.Configure(options => options.UseInMemoryTestQueries(queries).UseClosedRepositories("Generated.RelationshipKeys.Contracts")
            .UseGeneratedNamespace("Generated.RelationshipKeys.Persistence").UseScriptedTestDoubles("Generated.RelationshipKeys.TestDoubles"));
        if (mapped) ProxyNavigationRepositoryDesigner.Stores(builder);
        else NavigationReadRepositoryDesigner.Stores(builder);
        NavigationReadRepositoryDesigner.Queries(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        if (comparer) library.HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        library.HasQuery("Codes").Where<string>((row, code) => row.Code == code).OrderBy(row => row.Id).Select(row => row.Id).List();
        return builder;
    }

    internal static RepositoryModelBuilder ManyBuilder(bool mapped = true, bool optional = false, bool comparer = true)
    {
        var builder = optional ? NullTargetReadTestHarness.Builder(mapped) : ManyReadTestHarness.Builder(mapped);
        if (comparer) builder.Entity<ManyReadOwner>().HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        return builder;
    }

    private static Assembly CompileNavigation(bool mapped)
    {
        using var context = NavigationContext();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, NavigationBuilder(mapped).Build()));
    }
    private static Assembly CompileMany(bool mapped, bool optional)
    {
        using var context = ManyContext(optional);
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, ManyBuilder(mapped, optional).Build()));
    }

    internal static NavigationReadBook[] Books()
    {
        var rows = NavigationReadTestHarness.Books();
        foreach (var row in rows) row.LibraryCode = row.LibraryCode?.ToUpperInvariant();
        return rows;
    }
    internal static ManyReadLink[] Links(bool optional = false)
    {
        var rows = optional ? OptionalJoinTestHarness.Links() : ManyReadTestHarness.Links();
        foreach (var row in rows) row.OwnerCode = row.OwnerCode.ToUpperInvariant();
        return rows;
    }

    internal static (object Library, object Book, InMemoryDataContext Data) NavigationRepositories(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? NavigationMapped.Value : NavigationEntities.Value;
        const string ns = "Generated.RelationshipKeys.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + nameof(RelationshipNavigationContext) + "RepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            ProxyNavigationTestHarness.Seed(data);
            ProxyNavigationTestHarness.Store<ProxyNavigationBookData>(data, nameof(NavigationReadBook)).ReplaceWith(Books()
                .Select(row => new ProxyNavigationBookData(row.Id, row.LibraryCode, row.LibraryRegion, row.AuthorId, row.Price, row.Archived, row.ParentId)));
        }
        else
        {
            NavigationReadTestHarness.Seed(data);
            NavigationReadTestHarness.Store<NavigationReadBook>(data).ReplaceWith(Books());
        }
        return (Activator.CreateInstance(assembly.GetType(ns + nameof(NavigationReadLibrary) + "RepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + nameof(NavigationReadBook) + "RepositoryTestDouble")!, fixture)!, data);
    }

    internal static (object Owner, object Tag, InMemoryWriteDataContext Data) ManyRepositories(bool mapped = true, bool optional = false, Assembly? assembly = null)
    {
        assembly ??= optional ? (mapped ? OptionalMapped.Value : OptionalEntities.Value) : (mapped ? ManyMapped.Value : ManyEntities.Value);
        const string ns = "Generated.ManyRead.TestDoubles.";
        var contextName = optional ? nameof(RelationshipManyOptionalContext) : nameof(RelationshipManyContext);
        var fixture = Activator.CreateInstance(assembly.GetType(ns + contextName + "RepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            ManyReadTestHarness.OwnerStore(data).SeedRange(ManyReadTestHarness.Owners().Select(row => new ManyReadOwnerData(row.TenantId, row.Id, row.Code, row.Name, row.Hidden)));
            ManyReadTestHarness.TagStore(data).SeedRange(ManyReadTestHarness.Tags().Select(row => new ManyReadTagData(row.Id, row.Name, row.Hidden)));
            ManyReadTestHarness.LinkStore(data).SeedRange(Links(optional).Select(row => new ManyReadLinkData(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden)));
        }
        else
        {
            ManyReadTestHarness.Store<ManyReadOwner, (int, int)>(data, nameof(ManyReadOwner)).SeedRange(ManyReadTestHarness.Owners());
            ManyReadTestHarness.Store<ManyReadTag, int>(data, nameof(ManyReadTag)).SeedRange(ManyReadTestHarness.Tags());
            ManyReadTestHarness.Store<ManyReadLink, int>(data, nameof(ManyReadLink)).SeedRange(Links(optional));
        }
        return (Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryTestDouble")!, fixture)!, data);
    }
}
