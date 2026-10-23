using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class CompositeCollectionTestHarness
{
    private static readonly Lazy<Assembly> EntityGenerated = new(() => Compile(mapped: false));
    private static readonly Lazy<Assembly> MappedGenerated = new(() => Compile(mapped: true));
    internal const string Namespace = "Generated.CompositeCollections";

    internal static CompositeCollectionContext Context(string scenario = "valid") => new(new DbContextOptionsBuilder<CompositeCollectionContext>()
        .UseSqlite("Data Source=:memory:").ReplaceService<IModelCacheKeyFactory, CompositeCollectionModelCacheKeyFactory>().Options, scenario);

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool simulate = true, bool backend = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend).GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<CompositeCollectionParent>();
        if (mapped) parent.HasInMemoryStore<CompositeCollectionParentData, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Version))
            .MapInMemoryEntity<CompositeCollectionParentData>(row => new CompositeCollectionParent { TenantId = row.TenantId, Id = row.Id, Version = row.Version })
            .MapInMemoryWrites<CompositeCollectionParentData>((row, saved) => new(row.TenantId, row.Id, row.Version));
        else parent.HasInMemoryStore(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new CompositeCollectionParent { TenantId = row.TenantId, Id = row.Id, Version = row.Version });
        parent.HasQuery("Version").Where<CompositeCollectionRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.Id)
            .Select(row => row.Version).SingleOrDefault();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage" })
        {
            var command = parent.HasCommand(name).Where<CompositeCollectionRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.Id)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, CompositeCollectionRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, string), CompositeCollectionRequest>(row => row.Children, request => request.Items!,
                    row => new ValueTuple<string, string>(row.Code, row.Language), item => new ValueTuple<string, string>(item.Code, item.Language),
                    children => children.MaximumItems(5).AllowWrite(row => row.Id)
                        .Add(item => new CompositeCollectionChild { Id = item.Id, Code = item.Code, Language = item.Language, Text = item.Text, Version = 1, Hidden = false })
                        .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text).SetProperty(row => row.Version, row => row.Version + 1))
                        .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                        .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : name == "Reject" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate);
            if (name == "Stage") command.Stage(); else command.Save().Returns(row => row.Version);
        }
        var child = builder.Entity<CompositeCollectionChild>();
        if (mapped) child.HasInMemoryStore<CompositeCollectionChildData, int>(row => row.Id, row => new(row.Id, row.TenantId, row.ParentId, row.Code, row.Language, row.Text, row.Version, row.Hidden))
            .MapInMemoryEntity<CompositeCollectionChildData>(row => new CompositeCollectionChild { Id = row.Id, TenantId = row.TenantId, ParentId = row.ParentId,
                Code = row.Code, Language = row.Language, Text = row.Text, Version = row.Version, Hidden = row.Hidden })
            .MapInMemoryWrites<CompositeCollectionChildData>((row, saved) => new(row.Id, row.TenantId, row.ParentId, row.Code, row.Language, row.Text, row.Version, row.Hidden));
        else child.HasInMemoryStore(row => row.Id, row => new CompositeCollectionChild { Id = row.Id, TenantId = row.TenantId, ParentId = row.ParentId,
            Code = row.Code, Language = row.Language, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        child.HasVisibilityPolicy("Visible", row => !row.Hidden);
        child.HasQuery("List").Where<CompositeCollectionRequest>((row, request) => row.TenantId == request.TenantId && row.ParentId == request.Id)
            .OrderBy(row => row.Code).ThenBy(row => row.Language).Select(row => new CompositeCollectionView(row.Id, row.Code, row.Language, row.Text, row.Version)).List();
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Text, "shared")).Stage();
        return builder;
    }

    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? MappedGenerated.Value : EntityGenerated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.CompositeCollectionContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            Store<CompositeCollectionParentData, (int, int)>(data).SeedRange(Parents().Select(row => new CompositeCollectionParentData(row.TenantId, row.Id, row.Version)));
            Store<CompositeCollectionChildData, int>(data).SeedRange(Children().Select(row => new CompositeCollectionChildData(row.Id, row.TenantId, row.ParentId, row.Code, row.Language, row.Text, row.Version, row.Hidden)));
        }
        else { Store<CompositeCollectionParent, (int, int)>(data).SeedRange(Parents()); Store<CompositeCollectionChild, int>(data).SeedRange(Children()); }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.CompositeCollectionParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.CompositeCollectionChildRepositoryTestDouble")!, fixture)!, data);
    }

    internal static InMemoryStore<TRow, TKey> Store<TRow, TKey>(InMemoryWriteDataContext data) where TRow : class where TKey : notnull =>
        (InMemoryStore<TRow, TKey>)data.GetType().GetProperty((typeof(TRow) == typeof(CompositeCollectionParentData) ? nameof(CompositeCollectionParent) :
            typeof(TRow) == typeof(CompositeCollectionChildData) ? nameof(CompositeCollectionChild) : typeof(TRow).Name) + "Store")!.GetValue(data)!;
    internal static CompositeCollectionParent[] Parents() => [new() { TenantId = 10, Id = 1 }, new() { TenantId = 20, Id = 1 }];
    internal static CompositeCollectionChild[] Children() => [
        new() { Id = 1, TenantId = 10, ParentId = 1, Code = "title", Language = "en", Text = "old English" },
        new() { Id = 2, TenantId = 10, ParentId = 1, Code = "title", Language = "nl", Text = "old Dutch" },
        new() { Id = 3, TenantId = 10, ParentId = 1, Code = "summary", Language = "en", Text = "old summary" },
        new() { Id = 4, TenantId = 10, ParentId = 1, Code = "hidden", Language = "en", Text = "hidden", Hidden = true },
        new() { Id = 5, TenantId = 20, ParentId = 1, Code = "title", Language = "en", Text = "other tenant" }];

    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
}
