using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedCollectionTestHarness
{
    internal const string Namespace = "Generated.MappedCollections";
    private static readonly Lazy<Assembly> Generated = new(() => Compile(Builder()));
    internal static MappedCollectionContext Context() => new(new DbContextOptionsBuilder<MappedCollectionContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool simulate = true, bool backend = true, bool queries = true, bool strict = true, bool textSlot = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(queries)
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        var parent = builder.Entity<MappedCollectionParent>();
        parent.HasInMemoryStore<MappedCollectionParentSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Revision))
            .MapInMemoryEntity<MappedCollectionParentSeed>(row => new MappedCollectionParent { Id = row.Id, Title = row.Title, Counter = row.Counter })
            .MapInMemoryProperty<MappedCollectionParentSeed, int>("Revision", row => row.Revision)
            .MapInMemoryWrites<MappedCollectionParentSeed>((row, saved) => new(row.Id, row.Title, row.Counter, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
        parent.HasVisibilityPolicy("Visible", row => row.Title != "hidden");
        parent.HasQuery("Versions").OrderBy(row => row.Id).Select(row => EF.Property<int>(row, "Revision")).List();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage", "Saved" })
        {
            var command = parent.HasCommand(name).Where<MappedCollectionRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, MappedCollectionRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Counter, row => row.Counter + 1))
                .ExpectVersion<int, MappedCollectionRequest>("Revision", request => request.Revision)
                .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedCollectionRequest>(row => row.Children, request => request.Items,
                    row => row.Code, item => item.Code, children => children.MaximumItems(4).AllowWrite(row => row.Id)
                        .Add(item => new MappedCollectionChild { Id = item.Id, Code = item.Code, Text = item.Text, Counter = 0 })
                        .Update(setters => setters.SetPropertyWhen(row => row.Text, (row, item) => item.Text, item => item.Supplied)
                            .SetProperty(row => row.Counter, row => row.Counter + 1))
                        .ExpectVersion<int>("Revision", item => item.Revision)
                        .ExpectVersion<int?>("OptionalVersion", item => item.OptionalVersion)
                        .ExpectVersion<byte[]?>("Stamp", item => item.Stamp)
                        .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : name == "Reject"
                            ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate);
            if (name == "Stage")
            {
                command.Stage();
            }
            else if (name == "Saved" && queries)
            {
                command.Save().ReturnsSaved(row => new MappedCollectionView(row.Id, EF.Property<int>(row, "Revision"),
                    row.Children.Where(item => item.Text != "hidden").OrderBy(item => item.Id).Select(item => new MappedCollectionCard(item.Id, item.Code, item.Text, item.Category,
                        EF.Property<int>(item, "audit"), EF.Property<int>(item, "Revision"), EF.Property<int?>(item, "OptionalVersion"),
                        (byte[]?)item["Stamp"])).ToList()));
            }
            else
            {
                command.Save().ReturnsSaved(row => EF.Property<int>(row, "Revision"));
            }
        }
        parent.HasInMemoryNavigation(row => row.Children);
        var child = builder.Entity<MappedCollectionChild>();
        child.HasInMemoryStore<MappedCollectionChildSeed, int>(row => row.Id, row => new(row.Id, row.ParentId, row.Code, row.Text, row.Counter,
                row.Category, row.Audit, row.Revision, row.OptionalVersion, row.Stamp == null ? null : row.Stamp.ToArray(), row.Extra))
            .MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild
                { Id = row.Id, ParentId = row.ParentId, Code = row.Code, Text = row.Text, Counter = row.Counter })
            .MapInMemoryProperty<MappedCollectionChildSeed, string>(nameof(MappedCollectionChild.Category), row => row.Category)
            .MapInMemoryProperty<MappedCollectionChildSeed, int>("audit", row => row.Audit)
            .MapInMemoryProperty<MappedCollectionChildSeed, int>("Revision", row => row.Revision)
            .MapInMemoryProperty<MappedCollectionChildSeed, int?>("OptionalVersion", row => row.OptionalVersion)
            .MapInMemoryProperty<MappedCollectionChildSeed, byte[]?>("Stamp", row => row.Stamp);
        if (textSlot)
        {
            child.MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild
                { Id = row.Id, ParentId = row.ParentId, Code = row.Code, Counter = row.Counter })
                .MapInMemoryProperty<MappedCollectionChildSeed, string>(nameof(MappedCollectionChild.Text), row => row.Text);
        }
        ChildWriteMap(child);
        child.HasInMemoryGeneratedValue<string>(nameof(MappedCollectionChild.Category), (row, rows) => "entry")
            .HasInMemoryGeneratedValue<int>("audit", (row, rows) => 10)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1)
            .HasInMemoryGeneratedValue<int?>("OptionalVersion", (row, rows) => 1)
            .HasInMemoryGeneratedValue<byte[]?>("Stamp", (row, rows) => new byte[] { 1, 2 });
        child.HasVisibilityPolicy("Visible", row => row.Text != "hidden");
        child.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new MappedCollectionCard(row.Id, row.Code, row.Text, row.Category,
            EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"), (byte[]?)row["Stamp"])).List();
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).BypassVisibility("Visible")
            .Patch(setters => setters.SetProperty(row => row.Text, "independent")).Stage();
        return builder;
    }

    internal static void ChildWriteMap(RepositoryEntityBuilder<MappedCollectionChild> child, bool state = true)
        => child.MapInMemoryWrites<MappedCollectionChildSeed>((row, saved) => new(row.Id, row.ParentId, row.Code, row.Text, row.Counter, row.Category,
            EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"), (byte[]?)row["Stamp"],
            saved == null ? "created" : saved.Extra), useMappedPropertyState: state);

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Fixture(Assembly? assembly = null, string contextName = nameof(MappedCollectionContext))
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing." + contextName + "RepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ParentStore(data).SeedRange([new(1, "old", 2, 3), new(2, "other", 4, 5)]);
        ChildStore(data).SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedCollectionParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedCollectionChildRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<MappedCollectionParentSeed, int> ParentStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MappedCollectionParentSeed, int>)data.GetType().GetProperty("MappedCollectionParentStore")!.GetValue(data)!;
    internal static InMemoryStore<MappedCollectionChildSeed, int> ChildStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MappedCollectionChildSeed, int>)data.GetType().GetProperty("MappedCollectionChildStore")!.GetValue(data)!;
    internal static MappedCollectionChildSeed[] Seeds() =>
    [
        new(1, 1, "a", "old a", 2, "entry", 10, 3, 1, [1, 2]),
        new(2, 1, "b", "old b", 4, "entry", 10, 5, null, null),
        new(3, 1, "hidden", "hidden", 2, "entry", 10, 3, 1, [1, 2]),
        new(4, 2, "a", "other", 2, "entry", 10, 3, 1, [1, 2])
    ];
    internal static MappedCollectionRequest Request() => new(1, "changed", [new(1, "a", "changed a", Stamp: [1, 2]), new(20, "c", "created")]);
    internal static Task<T> Call<T>(object repository, string name, params object?[] inputs) => MappedStateTestHarness.Call<T>(repository, name + "Async", inputs);
}
