using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class FilteredCollectionTestHarness
{
    internal const string Namespace = "Generated.FilteredCollections";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    private static readonly string[] Commands = ["SaveItems", "StageItems", "KeepItems", "RejectItems", "Bypass", "SavedItems", "HideItems"];
    internal static FilteredCollectionContext Context() => new(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static RepositoryModelBuilder Builder(bool mapped = true, bool filters = true, bool root = true, bool simulate = true, bool backend = true, bool strict = true, bool translations = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(backend)
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        var parent = builder.Entity<MemoryCollectionParent>(); var child = builder.Entity<MemoryCollectionChild>();
        if (mapped)
        {
            parent.HasInMemoryStore<FilteredCollectionParentSeed, int>(row => row.Id, row => new(row.Id, row.OwnerId, row.Name, row.Version))
                .MapInMemoryEntity<FilteredCollectionParentSeed>(row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<FilteredCollectionParentSeed>((row, saved) => new(row.Id, row.OwnerId, row.Name, row.Version));
            child.HasInMemoryStore<FilteredCollectionChildSeed, int>(row => row.Id, row => new(row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden))
                .MapInMemoryEntity<FilteredCollectionChildSeed>(row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<FilteredCollectionChildSeed>((row, saved) => new(row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden));
        }
        else
        {
            parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
            child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        }
        child.HasInMemoryGeneratedValue(row => row.Id, (row, rows) => rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        parent.HasVisibilityPolicy("Visible", row => row.Name != "private");
        parent.UseInMemoryModelQueryFilter(includeNavigations: true).HasInMemoryNavigation(row => row.Children);
        child.UseInMemoryModelQueryFilter(includeNavigations: true).HasVisibilityPolicy("Visible", row => !row.Hidden);
        foreach (var name in Commands)
        {
            var command = parent.HasCommand(name).Where<MemoryCollectionRequest>((row, input) => row.Id == input.Id && row.OwnerId == input.OwnerId)
                .Patch(setters => setters.SetProperty<string, MemoryCollectionRequest>(row => row.Name, (row, input) => input.Name)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, MemoryCollectionRequest>(row => row.Version, (row, input) => input.ExpectedVersion)
                .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, input => input.Items!, row => row.Key, item => item.Key,
                    collection => collection.MaximumItems(3).Add(item => new MemoryCollectionChild { Key = item.Key, Text = item.Text, Version = 1, Hidden = false })
                        .Update(setters => setters.SetPropertyWhen(row => row.Text, (row, item) => item.Text, item => item.Supplied)
                            .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                        .ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                        .MissingItems(name == "KeepItems" ? RepositoryCollectionRemovalBehavior.Keep : name == "RejectItems" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate, includeModelQueryFilters: filters).UseInMemoryModelQueryFilter(root, includeNavigations: name == "SavedItems")
                .UseInMemoryTranslations(translations);
            if (name == "Bypass") command.BypassVisibility("Visible");
            if (name == "StageItems") command.Stage();
            else if (name == "SavedItems") command.Save().ReturnsSaved(row => new FilteredCollectionView(row.Id, row.Name,
                row.Children.OrderBy(item => item.Id).Select(item => item.Text).ToList()));
            else command.Save().Returns(row => new MemoryCollectionView(row.Id, row.Name, row.Version));
        }
        parent.HasCommand("Optional").Where<MemoryCollectionOptionalRequest>((row, input) => row.Id == input.Id)
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionOptionalRequest>(row => row.Children, input => input.Items.Value!, row => row.Key, item => item.Key,
                collection => collection.MaximumItems(3).When<MemoryCollectionOptionalRequest>(input => input.Items.IsSpecified)
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .UseInMemoryModelQueryFilter(root).SimulateCollectionWrites(simulate, includeModelQueryFilters: filters).UseInMemoryTranslations(translations).Save();
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).BypassVisibility("Visible")
            .Patch(setters => setters.SetProperty(row => row.Text, "pending")).UseInMemoryModelQueryFilter().UseInMemoryTranslations(translations).Stage();
        return builder;
    }
    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Fixture(bool mapped = true, Assembly? assembly = null, string contextName = nameof(FilteredCollectionContext))
    {
        assembly ??= mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing." + contextName + "RepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Seed(data, mapped);
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionChildRepositoryTestDouble")!, fixture)!, data);
    }
    internal static void Seed(InMemoryWriteDataContext data, bool mapped)
    {
        if (mapped)
        {
            Store<FilteredCollectionParentSeed>(data, nameof(MemoryCollectionParent)).SeedRange(Parents().Select(row => new FilteredCollectionParentSeed(row.Id, row.OwnerId, row.Name, row.Version)));
            Store<FilteredCollectionChildSeed>(data, nameof(MemoryCollectionChild)).SeedRange(Children().Select(row => new FilteredCollectionChildSeed(row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden)));
        }
        else
        {
            Store<MemoryCollectionParent>(data, nameof(MemoryCollectionParent)).SeedRange(Parents());
            Store<MemoryCollectionChild>(data, nameof(MemoryCollectionChild)).SeedRange(Children());
        }
    }
    internal static MemoryCollectionParent[] Parents() => [new() { Id = 1, OwnerId = 10, Name = "one" }, new() { Id = 2, OwnerId = 20, Name = "two" }, new() { Id = 3, OwnerId = -1, Name = "filtered" }];
    internal static MemoryCollectionChild[] Children() =>
    [
        new() { Id = 1, ParentId = 1, Key = "a", Text = "A" }, new() { Id = 2, ParentId = 1, Key = "b", Text = "B" },
        new() { Id = 3, ParentId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new() { Id = 4, ParentId = 2, Key = "other", Text = "other" },
        new() { Id = 5, ParentId = 1, Key = "deleted", Text = "deleted" }, new() { Id = 6, ParentId = 3, Key = "blocked", Text = "blocked" }
    ];
    internal static InMemoryStore<TRow, int> Store<TRow>(InMemoryDataContext data, string entity) where TRow : class
        => (InMemoryStore<TRow, int>)data.GetType().GetProperty(entity + "Store")!.GetValue(data)!;
    internal static FilteredCollectionChildSeed[] Rows(InMemoryDataContext data, bool mapped) => mapped ? Store<FilteredCollectionChildSeed>(data, nameof(MemoryCollectionChild)).Snapshot().OrderBy(row => row.Id).ToArray()
        : Store<MemoryCollectionChild>(data, nameof(MemoryCollectionChild)).Snapshot().Select(row => new FilteredCollectionChildSeed(row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden)).OrderBy(row => row.Id).ToArray();
    internal static T Invoke<T>(object repo, string method, params object?[] input) => (T)repo.GetType().GetMethod(method + "Async")!.Invoke(repo, input.Append((object)CancellationToken.None).ToArray())!;
}
