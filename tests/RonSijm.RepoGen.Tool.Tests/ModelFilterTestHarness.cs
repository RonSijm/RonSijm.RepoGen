using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ModelFilterTestHarness
{
    internal const string Namespace = "Generated.ModelFilters";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static ModelFilterContext Context() => new(new DbContextOptionsBuilder<ModelFilterContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Base(bool mapped = true, bool enabled = true, bool strict = true, bool queries = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries).RequirePortableTestQueries(strict && queries));
        var row = builder.Entity<ModelFilterRow>(); row.WriteEnabled = false;
        row.UseInMemoryModelQueryFilter(enabled);
        if (mapped) row.HasInMemoryStore<ModelFilterRowSeed, int>(item => item.Id, item => new(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId))
            .MapInMemoryEntity<ModelFilterRowSeed>(item => new ModelFilterRow { Id = item.Id, Title = item.Title, Deleted = item.Deleted, Hidden = item.Hidden, FolderId = item.FolderId });
        else row.HasInMemoryStore(item => item.Id, item => new ModelFilterRow { Id = item.Id, Title = item.Title, Deleted = item.Deleted, Hidden = item.Hidden, FolderId = item.FolderId });
        row.HasVisibilityPolicy("Visible", item => !item.Hidden);
        var folder = builder.Entity<ModelFilterFolder>(); folder.WriteEnabled = false;
        folder.UseInMemoryModelQueryFilter(enabled);
        if (mapped) folder.HasInMemoryStore<ModelFilterFolderSeed, int>(item => item.Id, item => new(item.Id, item.Archived))
            .MapInMemoryEntity<ModelFilterFolderSeed>(item => new ModelFilterFolder { Id = item.Id, Archived = item.Archived });
        else folder.HasInMemoryStore(item => item.Id, item => new ModelFilterFolder { Id = item.Id, Archived = item.Archived });
        return builder;
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool enabled = true, bool strict = true, bool queries = true)
    {
        var builder = Base(mapped, enabled, strict, queries); var entity = builder.Entity<ModelFilterRow>();
        entity.HasProjection<ModelFilterCard>(row => new ModelFilterCard(row.Id, row.Title));
        entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<ModelFilterCard>().List();
        entity.HasQuery("Get").Where<int>((row, id) => row.Id == id).ProjectTo<ModelFilterCard>().SingleOrDefault();
        entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<ModelFilterCard>().Paged();
        entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ModelFilterCard>().Stream();
        entity.HasQuery("Count").Count();
        entity.HasQuery("Exists").Where<int>((row, id) => row.Id == id).Exists();
        entity.HasQuery("Titles").OrderBy(row => row.Id).ProjectTo<ModelFilterCard>().Results<ModelFilterCard>().Dictionary(row => row.Id, row => row.Title);
        entity.HasQuery("Folders").GroupBy(row => row.FolderId, group => group.Count()).List();
        entity.HasQuery("IncludingDeleted").IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("IncludingHidden").BypassVisibility("Visible").OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Joined").From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterFolder>(),
            row => row.FolderId, folder => (int?)folder.Id, (row, folder) => row.Id)).OrderBy(id => id).List();
        entity.HasQuery("JoinedAll").IgnoreQueryFilters().From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterFolder>(),
            row => row.FolderId, folder => (int?)folder.Id, (row, folder) => row.Id)).OrderBy(id => id).List();
        entity.HasQuery("Correlated").From(sources => sources.Set<ModelFilterRow>()
            .Where(row => sources.Set<ModelFilterFolder>().Any(folder => folder.Id == row.FolderId)).Select(row => row.Id)).OrderBy(id => id).List();
        entity.HasQuery("Self").From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterRow>(),
            left => left.Id, right => right.Id, (left, right) => left.Id)).OrderBy(id => id).List();
        entity.HasQuery("Union").From(sources => sources.Set<ModelFilterRow>().Select(row => row.Id)
            .Union(sources.Set<ModelFilterFolder>().Select(folder => folder.Id))).OrderBy(id => id).List();
        entity.HasQuery("JoinedPage").From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterFolder>(),
            row => row.FolderId, folder => (int?)folder.Id, (row, folder) => row.Id)).OrderBy(id => id).Paged();
        return builder;
    }

    internal static ModelFilterRow[] Seeds() => [new() { Id = 1, Title = "one", FolderId = 1 }, new() { Id = 2, Title = "deleted", Deleted = true, FolderId = 1 },
        new() { Id = 3, Title = "hidden", Hidden = true, FolderId = 1 }, new() { Id = 4, Title = "archived-folder", FolderId = 2 }, new() { Id = 5, Title = "orphan" }];
    internal static ModelFilterFolder[] Folders() => [new() { Id = 1 }, new() { Id = 2, Archived = true }];

    internal static (object Repository, InMemoryDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterContextRepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!;
        if (mapped)
        {
            Store<ModelFilterRowSeed>(data, "ModelFilterRow").SeedRange(Seeds().Select(row => new ModelFilterRowSeed(row.Id, row.Title, row.Deleted, row.Hidden, row.FolderId)));
            Store<ModelFilterFolderSeed>(data, "ModelFilterFolder").SeedRange(Folders().Select(folder => new ModelFilterFolderSeed(folder.Id, folder.Archived)));
        }
        else
        {
            Store<ModelFilterRow>(data, "ModelFilterRow").SeedRange(Seeds());
            Store<ModelFilterFolder>(data, "ModelFilterFolder").SeedRange(Folders());
        }
        return (repository, data);
    }
    internal static InMemoryStore<T, int> Store<T>(InMemoryDataContext data, string name) where T : class
        => (InMemoryStore<T, int>)data.GetType().GetProperty(name + "Store")!.GetValue(data)!;
}
