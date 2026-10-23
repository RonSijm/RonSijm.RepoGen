using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ContextFilterTestHarness
{
    internal const string Namespace = "Generated.ContextFilters";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static ContextFilterContext Context() => new(new DbContextOptionsBuilder<ContextFilterContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Base(bool mapped = true, bool bound = true, bool strict = true, bool backend = true, bool enabled = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseKeyedContextLeases<ContextFilterKey>().UseReturningWrites<SqliteReturningWriter>()
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores()
            .UseInMemoryTestQueries(backend).UseInMemoryTestCommands(backend)
            .RequirePortableTestQueries(strict && backend).RequirePortableTestCommands(strict && backend));
        if (bound) Bind(builder);
        var row = builder.Entity<ModelFilterRow>();
        row.UseInMemoryModelQueryFilter(enabled, includeNavigations: enabled);
        row.HasInMemoryNavigation(item => item.Folder);
        if (mapped) row.HasInMemoryStore<ModelFilterRowSeed, int>(item => item.Id, item => new(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId))
            .MapInMemoryEntity<ModelFilterRowSeed>(item => new ModelFilterRow { Id = item.Id, Title = item.Title, Deleted = item.Deleted, Hidden = item.Hidden, FolderId = item.FolderId })
            .MapInMemoryWrites<ModelFilterRowSeed>((item, saved) => new(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId));
        else row.HasInMemoryStore(item => item.Id, item => new ModelFilterRow { Id = item.Id, Title = item.Title, Deleted = item.Deleted, Hidden = item.Hidden, FolderId = item.FolderId });
        row.HasVisibilityPolicy("Visible", item => !item.Hidden);
        var folder = builder.Entity<ModelFilterFolder>(); folder.WriteEnabled = false;
        folder.UseInMemoryModelQueryFilter(enabled, includeNavigations: enabled).HasInMemoryNavigation(item => item.Rows);
        if (mapped) folder.HasInMemoryStore<ModelFilterFolderSeed, int>(item => item.Id, item => new(item.Id, item.Archived))
            .MapInMemoryEntity<ModelFilterFolderSeed>(item => new ModelFilterFolder { Id = item.Id, Archived = item.Archived });
        else folder.HasInMemoryStore(item => item.Id, item => new ModelFilterFolder { Id = item.Id, Archived = item.Archived });
        return builder;
    }

    internal static void Bind(RepositoryModelBuilder builder)
    {
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(context => context.Tenant, key => key.Tenant);
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, int>(context => context.FolderId, key => key.FolderId);
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, bool>(context => context.IncludeDeleted, key => key.IncludeDeleted);
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool bound = true, bool strict = true, bool backend = true, bool enabled = true)
    {
        var builder = Base(mapped, bound, strict, backend, enabled); var row = builder.Entity<ModelFilterRow>();
        foreach (var name in new[] { "Cards", "All", "Bypass" })
        {
            var query = row.HasQuery(name).UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
                .Where<ContextFilterRequest>((item, input) => input.Id == 0 || item.Id == input.Id).OrderBy(item => item.Id);
            if (name == "All") query.IgnoreQueryFilters();
            if (name == "Bypass") query.BypassVisibility("Visible");
            query.Select(item => new ModelFilterCard(item.Id, item.Title)).List();
        }
        row.HasQuery("Page").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).OrderBy(item => item.Id)
            .Select(item => new ModelFilterCard(item.Id, item.Title)).Paged();
        row.HasQuery("Count").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).Count();
        row.HasQuery("Joined").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .From(sources => sources.Set<ModelFilterRow>().Join(sources.Set<ModelFilterFolder>(), item => item.FolderId,
                folder => (int?)folder.Id, (item, folder) => item.Id)).OrderBy(id => id).List();
        row.HasQuery("Related").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).OrderBy(item => item.Id)
            .Select(item => new ContextFilterCard(item.Id, item.Title, item.Folder == null ? null : (int?)item.Folder.Id)).List();
        builder.Entity<ModelFilterFolder>().HasQuery("Collections").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Select(folder => folder.Rows.Count()).List();
        foreach (var name in new[] { "Rename", "RenameSaved", "RenameGraphSaved", "StageRename" })
        {
            var command = row.HasCommand(name).UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
                .Where<ContextFilterRequest>((item, input) => item.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, ContextFilterRequest>(item => item.Title, (item, input) => input.Title))
                .UseInMemoryModelQueryFilter(enabled);
            if (name == "StageRename") command.Stage();
            else if (name == "RenameGraphSaved") command.Save().ReturnsSaved(item => new ContextFilterCard(item.Id, item.Title,
                item.Folder == null ? null : (int?)item.Folder.Id)).UseInMemoryModelQueryFilter(enabled, includeNavigations: enabled);
            else if (name == "RenameSaved") command.Save().ReturnsSaved(item => new ModelFilterCard(item.Id, item.Title));
            else command.Save().Returns(item => new ModelFilterCard(item.Id, item.Title));
        }
        row.HasCommand("RenameMany").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .PatchMany<ContextFilterRequest, int, ContextFilterRequest>(input => input.Items, item => item.Id, input => input.Id,
                setters => setters.SetProperty(item => item.Title, (item, input) => input.Title), maximumItems: 3)
            .UseInMemoryModelQueryFilter(enabled).Save().ReturnsSaved(item => new ModelFilterCard(item.Id, item.Title));
        row.HasCommand("StageDelete").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Where<ContextFilterRequest>((item, input) => item.Id == input.Id).TrackedDelete().UseInMemoryModelQueryFilter(enabled).Stage();
        row.HasCommand("Archive").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Where<ContextFilterRequest>((item, input) => input.Id == 0 || item.Id == input.Id).Update(setters => setters.SetProperty(item => item.Deleted, true))
            .ReturnsMany(item => new ModelFilterCard(item.Id, item.Title)).SimulateImmediateWrites().UseInMemoryModelQueryFilter(enabled);
        row.HasCommand("Consume").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Where<ContextFilterRequest>((item, input) => item.Id == input.Id).Delete().SimulateImmediateWrites().UseInMemoryModelQueryFilter(enabled);
        return builder;
    }

    internal static ModelFilterRow[] Seeds() => [new() { Id = 1, Title = "A", FolderId = 1 }, new() { Id = 2, Title = "B", FolderId = 1 },
        new() { Id = 3, Title = "A", Deleted = true, FolderId = 1 }, new() { Id = 4, Title = "A", Hidden = true, FolderId = 1 },
        new() { Id = 5, Title = "A", FolderId = 2 }, new() { Id = 6, Title = "A" }];

    internal static (object Row, object Folder, InMemoryWriteDataContext Data, object Fixture) Fixture(ContextFilterKey? key = null, bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value; key ??= new("A");
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ContextFilterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!;
        Seed(data, mapped);
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterFolderRepositoryTestDouble")!, fixture)!, data, fixture);
    }

    internal static void Seed(InMemoryWriteDataContext data, bool mapped)
    {
        if (mapped)
        {
            ModelFilterTestHarness.Store<ModelFilterRowSeed>(data, "ModelFilterRow").ReplaceWith(Seeds().Select(item => new ModelFilterRowSeed(item.Id, item.Title, item.Deleted, item.Hidden, item.FolderId)));
            ModelFilterTestHarness.Store<ModelFilterFolderSeed>(data, "ModelFilterFolder").ReplaceWith([new(1, false), new(2, false)]);
        }
        else
        {
            ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").ReplaceWith(Seeds());
            ModelFilterTestHarness.Store<ModelFilterFolder>(data, "ModelFilterFolder").ReplaceWith([new() { Id = 1 }, new() { Id = 2 }]);
        }
    }
    internal static Task<T> Call<T>(object repository, string name, params object?[] input) => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", input);
}
