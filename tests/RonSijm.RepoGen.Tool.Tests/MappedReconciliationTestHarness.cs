using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedReconciliationTestHarness
{
    internal const string Namespace = "Generated.MappedReconciliation";
    private static readonly Lazy<Assembly> Production = new(() =>
    {
        using var context = Context();
        return NativeSavedTestHarness.CompileProduction(context, Builder());
    });
    private static readonly Lazy<Assembly> Testing = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });

    internal static MappedReconciliationContext Context() => new(new DbContextOptionsBuilder<MappedReconciliationContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder BareBuilder() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .GenerateOnlyConfiguredEntities().UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence"));

    internal static RepositoryModelBuilder Builder(bool parentState = true, bool childState = true, bool simulate = true,
        bool backend = true, bool strict = true)
    {
        var builder = BareBuilder().Configure(options => options.UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores()
            .UseInMemoryTestQueries().UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict));
        var parent = builder.Entity<MappedCollectionParent>();
        parent.HasInMemoryStore<MappedCollectionParentSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Counter, row.Revision))
            .MapInMemoryEntity<MappedCollectionParentSeed>(row => new MappedCollectionParent { Id = row.Id, Title = row.Title, Counter = row.Counter })
            .MapInMemoryProperty<MappedCollectionParentSeed, int>("Revision", row => row.Revision);
        if (parentState) parent.MapInMemoryWrites<MappedCollectionParentSeed>((row, saved) => new(row.Id, row.Title, row.Counter,
            EF.Property<int>(row, "Revision")), useMappedPropertyState: true);
        parent.HasVisibilityPolicy("Visible", row => row.Title != "hidden");
        parent.HasInMemoryNavigation(row => row.Children);
        parent.HasQuery("Snapshots").OrderBy(row => row.Id).Select(Projection).List();
        foreach (var name in new[] { "Edit", "Stage", "DeleteMissing", "RejectMissing", "Current", "Conditional", "FailValue", "FailSave" })
        {
            var command = parent.HasCommand(name).Where<MappedReconciliationRequest>((row, request) => row.Id == request.Id);
            if (name != "Conditional")
                command.Patch(setters => setters.SetProperty<string, MappedReconciliationRequest>(nameof(MappedCollectionParent.Title), (row, request) => request.Title)
                        .SetProperty<int>(nameof(MappedCollectionParent.Counter), row => row.Counter + 1)
                        .SetProperty<int, MappedReconciliationRequest>("Revision", (row, request) => request.Revision + 1))
                    .ExpectVersion<int, MappedReconciliationRequest>("Revision", request => request.Revision).AllowWrite<int>("Revision");
            command.ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children,
                request => request.Items, child => child.Code, item => item.Code, children =>
                {
                    children.MaximumItems(3).When<MappedReconciliationRequest>(request => request.IncludeChildren)
                        .AllowWrite<string>(nameof(MappedCollectionChild.Category));
                    children.Update(setters =>
                    {
                        if (name == "Conditional")
                        {
                            setters.SetPropertyWhen<string>(nameof(MappedCollectionChild.Category), (child, item) => item.Text, item => item.Supplied);
                            return;
                        }
                        if (name == "Current") setters.SetProperty<int>("audit", 40);
                        else if (name == "FailValue") setters.SetProperty<int>("audit", (child, item) => 100 / (item.Revision - 5));
                        else setters.SetProperty<int>("audit", child => EF.Property<int>(child, "audit") + 1);
                        if (name == "Current") setters.SetProperty(nameof(MappedCollectionChild.Category), child => child.Category + EF.Property<int>(child, "audit"));
                        else if (name == "FailSave") setters.SetProperty<string>(nameof(MappedCollectionChild.Category), (child, item) => item.Text == "invalid" ? null! : item.Text);
                        else setters.SetPropertyWhen<string>(nameof(MappedCollectionChild.Category), (child, item) => item.Text, item => item.Supplied);
                        setters.SetPropertyWhen(child => child.Text, (child, item) => item.Text, item => item.Supplied)
                            .SetProperty<int>("Revision", (child, item) => item.Revision + 1)
                            .SetProperty<int?>("OptionalVersion", (child, item) => item.OptionalVersion == null ? null : item.OptionalVersion + 1);
                        if (name == "Current") setters.SetProperty<byte[]?>(MappedReconciliationContext.Stamp, child => (byte[]?)child[MappedReconciliationContext.Stamp]);
                        else setters.SetProperty<byte[]?>(MappedReconciliationContext.Stamp, (child, item) => item.Supplied ? null : item.Stamp);
                    });
                    if (name != "Conditional") children.AllowWrite<int>("Revision").AllowWrite<int?>("OptionalVersion")
                        .AllowWrite<byte[]?>(MappedReconciliationContext.Stamp)
                        .ExpectVersion<int>("Revision", item => item.Revision)
                        .ExpectVersion<int?>("OptionalVersion", item => item.OptionalVersion)
                        .ExpectVersion<byte[]?>(MappedReconciliationContext.Stamp, item => item.Stamp);
                    children.MissingItems(name == "DeleteMissing" ? RepositoryCollectionRemovalBehavior.Delete : name == "RejectMissing"
                        ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Keep);
                }).SimulateCollectionWrites(simulate);
            if (name == "Stage") command.Stage(); else command.Save().ReturnsSaved(Projection);
            if (name == "FailSave") command.RequireTestOverride("The provider's NOT NULL constraint is not simulated.");
        }
        var child = builder.Entity<MappedCollectionChild>().ProtectWrite<string>(nameof(MappedCollectionChild.Category));
        child.HasInMemoryStore<MappedCollectionChildSeed, int>(row => row.Id, row => new(row.Id, row.ParentId, row.Code, row.Text, row.Counter,
                row.Category, row.Audit, row.Revision, row.OptionalVersion, row.Stamp == null ? null : row.Stamp.ToArray(), row.Extra))
            .MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild { Id = row.Id, ParentId = row.ParentId, Code = row.Code, Text = row.Text, Counter = row.Counter })
            .MapInMemoryProperty<MappedCollectionChildSeed, string>(nameof(MappedCollectionChild.Category), row => row.Category)
            .MapInMemoryProperty<MappedCollectionChildSeed, int>("audit", row => row.Audit)
            .MapInMemoryProperty<MappedCollectionChildSeed, int>("Revision", row => row.Revision)
            .MapInMemoryProperty<MappedCollectionChildSeed, int?>("OptionalVersion", row => row.OptionalVersion)
            .MapInMemoryProperty<MappedCollectionChildSeed, byte[]?>(MappedReconciliationContext.Stamp, row => row.Stamp);
        if (childState) child.MapInMemoryWrites<MappedCollectionChildSeed>((row, saved) => new(row.Id, row.ParentId, row.Code, row.Text, row.Counter,
            row.Category, EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"),
            (byte[]?)row[MappedReconciliationContext.Stamp], saved == null ? "created" : saved.Extra), useMappedPropertyState: true);
        child.HasVisibilityPolicy("Visible", row => row.Text != "hidden");
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).BypassVisibility("Visible")
            .Patch(setters => setters.SetProperty(row => row.Text, "independent")).Stage();
        return builder;
    }

    internal static Expression<Func<MappedCollectionParent, MappedCollectionView>> Projection => row => new MappedCollectionView(row.Id,
        EF.Property<int>(row, "Revision"), row.Children.Where(child => child.Text != "hidden").OrderBy(child => child.Id)
            .Select(child => new MappedCollectionCard(child.Id, child.Code, child.Text, child.Category, EF.Property<int>(child, "audit"),
                EF.Property<int>(child, "Revision"), EF.Property<int?>(child, "OptionalVersion"), (byte[]?)child[MappedReconciliationContext.Stamp])).ToList());

    internal static MappedReconciliationRequest Request() => new(1, "changed", [new(1, "a", "edited a", Stamp: [1, 2]), new(2, "b", "edited b", 5, null)]);
    internal static async Task SeedAsync(MappedReconciliationContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in new MappedCollectionParentSeed[] { new(1, "old", 2, 3), new(2, "other", 4, 5) })
        {
            var row = new MappedCollectionParent { Id = seed.Id, Title = seed.Title, Counter = seed.Counter };
            context.Add(row);
            context.Entry(row).Property("Revision").CurrentValue = seed.Revision;
        }
        foreach (var seed in MappedCollectionTestHarness.Seeds())
        {
            var row = new MappedCollectionChild { Id = seed.Id, ParentId = seed.ParentId, Code = seed.Code, Text = seed.Text, Counter = seed.Counter };
            context.Add(row);
            var entry = context.Entry(row);
            entry.Property(nameof(MappedCollectionChild.Category)).CurrentValue = seed.Category;
            entry.Property("audit").CurrentValue = seed.Audit;
            entry.Property("Revision").CurrentValue = seed.Revision;
            entry.Property("OptionalVersion").CurrentValue = seed.OptionalVersion;
            entry.Property(MappedReconciliationContext.Stamp).CurrentValue = seed.Stamp;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    internal static object Operations(MappedReconciliationContext context, bool child = false, Assembly? assembly = null)
        => Activator.CreateInstance((assembly ?? Production.Value).GetType(Namespace + ".Persistence.MappedCollection" + (child ? "Child" : "Parent") + "RepositoryOperations")!, context)!;
    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Fixture(Assembly? assembly = null)
    {
        var fixture = Activator.CreateInstance((assembly ?? Testing.Value).GetType(Namespace + ".Testing.MappedReconciliationContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ParentStore(data).SeedRange([new(1, "old", 2, 3), new(2, "other", 4, 5)]);
        ChildStore(data).SeedRange(MappedCollectionTestHarness.Seeds());
        return (Activator.CreateInstance((assembly ?? Testing.Value).GetType(Namespace + ".Testing.MappedCollectionParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance((assembly ?? Testing.Value).GetType(Namespace + ".Testing.MappedCollectionChildRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<MappedCollectionParentSeed, int> ParentStore(InMemoryWriteDataContext data) => MappedCollectionTestHarness.ParentStore(data);
    internal static InMemoryStore<MappedCollectionChildSeed, int> ChildStore(InMemoryWriteDataContext data) => MappedCollectionTestHarness.ChildStore(data);
    internal static Task<T> Call<T>(object repository, string method, params object?[] inputs) => MappedStateTestHarness.Call<T>(repository, method + "Async", inputs);
}
