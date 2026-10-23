using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class AtomicSavedTestHarness
{
    internal const string Namespace = "Generated.AtomicSaved";
    private static readonly Lazy<Assembly> Generated = new(() => Compile(Builder()));

    internal static RepositoryModelBuilder Builder(bool navigations = true, bool queries = true, bool backend = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseAtomicWrites<SqliteAtomicWriter>().UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict));
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        var child = builder.Entity<MemoryCollectionChild>();
        child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild
            { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        child.HasInMemoryGeneratedValue(row => row.Id, (row, rows) => checked(rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
        child.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (navigations)
        {
            parent.HasInMemoryNavigation(row => row.Children);
            child.HasInMemoryNavigation(row => row.Parent);
        }
        foreach (var name in new[] { "Ensure", "Put", "FailEnsure", "FailPut", "Hide", "BypassHide" })
        {
            var command = child.HasCommand(name).Create<AtomicSavedRequest>(request => new MemoryCollectionChild
                { ParentId = request.ParentId, Key = request.Key, Text = request.Text, Version = 1, Hidden = request.Hidden })
                .AllowWrite(row => row.ParentId).AllowWrite(row => row.Version);
            if (name.EndsWith("Ensure", StringComparison.Ordinal))
            {
                command.InsertIfAbsent(row => new { row.ParentId, row.Key });
            }
            else
            {
                command.InsertOrUpdate(row => new { row.ParentId, row.Key }, setters => setters
                    .SetProperty<string, AtomicSavedRequest>(row => row.Text, (row, request) => request.Text)
                    .SetProperty<bool, AtomicSavedRequest>(row => row.Hidden, (row, request) => request.Hidden)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                    .ExpectVersion<int, AtomicSavedRequest>(row => row.Version, (row, request) => request.ExpectedVersion);
            }
            if (name.StartsWith("Fail", StringComparison.Ordinal))
            {
                command.ReturnsSaved(row => new SavedResultRejectedView(row.Text));
            }
            else
            {
                command.ReturnsSaved<AtomicSavedView, AtomicSavedRequest>((row, request) => new AtomicSavedView(row.Id, row.Text, row.Version,
                    row.Parent == null ? "missing" : row.Parent.Name, request.Text,
                    row.Parent == null ? new List<string>() : row.Parent.Children.Where(sibling => !sibling.Hidden)
                        .OrderBy(sibling => sibling.Key).Select(sibling => sibling.Text).ToList()));
            }
            if (name == "BypassHide")
            {
                command.BypassVisibility("Visible");
            }
            command.SimulateImmediateWrites();
        }
        parent.HasCommand("StageName").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "pending parent")).Stage();
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Text, "pending child")).Stage();
        return builder;
    }

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = MemoryCollectionTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    internal static (object Child, object Parent, InMemoryWriteDataContext Data) Fixture(Assembly? assembly = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        MemoryCollectionTestHarness.ParentStore(data).SeedRange(Parents());
        MemoryCollectionTestHarness.ChildStore(data).SeedRange(Children());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionChildRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionParentRepositoryTestDouble")!, fixture)!, data);
    }

    internal static MemoryCollectionParent[] Parents() => [new() { Id = 1, OwnerId = 10, Name = "saved parent" }];
    internal static MemoryCollectionChild[] Children() =>
    [
        new() { Id = 1, ParentId = 1, Key = "a", Text = "A" },
        new() { Id = 2, ParentId = 1, Key = "b", Text = "B" },
        new() { Id = 3, ParentId = 1, Key = "hidden", Text = "secret", Hidden = true }
    ];

    internal static Task<TResult> Call<TResult>(object repository, string operation, object request, CancellationToken token = default)
        => (Task<TResult>)repository.GetType().GetMethod(operation + "Async")!.Invoke(repository, [request, token])!;

    internal static async Task SeedAsync(MemoryCollectionContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Parents());
        context.AddRange(Children());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
