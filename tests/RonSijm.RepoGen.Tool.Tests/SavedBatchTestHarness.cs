using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class SavedBatchTestHarness
{
    private static readonly string[] Commands = ["Refresh", "Unversioned", "Hide", "BypassHide", "Fail"];
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });

    internal static SavedBatchContext Context() => new(new DbContextOptionsBuilder<SavedBatchContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool navigation = true, bool queries = true, bool backend = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.SavedBatch.Contracts")
            .UseGeneratedNamespace("Generated.SavedBatch.Persistence").UseScriptedTestDoubles("Generated.SavedBatch.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict).GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<SavedBatchParent>();
        parent.HasInMemoryStore<SavedBatchData, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Code, row.Name, row.Version, row.Hidden))
            .MapInMemoryEntity<SavedBatchData>(row => new SavedBatchParent { TenantId = row.TenantId, Id = row.Id, Code = row.Code, Name = row.Name, Version = row.Version, Hidden = row.Hidden })
            .MapInMemoryWrites<SavedBatchData>((row, saved) => new(row.TenantId, row.Id, row.Code, row.Name, row.Version, row.Hidden));
        parent.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (navigation) parent.HasInMemoryNavigation(row => row.Children);
        foreach (var name in Commands)
        {
            var command = parent.HasCommand(name);
            if (name == "Refresh") command.Where(row => row.Name == "old");
            command.PatchMany<SavedBatchItem, string, SavedBatchRequest>(request => request.Items, row => row.Code, item => item.Code, setters =>
            {
                setters.SetPropertyWhen(row => row.Name, (row, item) => item.Name, item => item.Supplied);
                if (name != "Unversioned") setters.SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion);
                if (name is "Hide" or "BypassHide") setters.SetProperty(row => row.Hidden, true);
            }, maximumItems: 3).AllowWrite(row => row.Version).Save();
            if (name == "Fail") command.ReturnsSaved(row => new SavedResultRejectedView(row.Name));
            else command.ReturnsSaved<SavedBatchView, SavedBatchRequest>((row, request) => new SavedBatchView(row.TenantId, row.Id, row.Code, row.Name, row.Version, request.Label,
                row.Children.Where(child => !child.Hidden).OrderBy(child => child.Id).Select(child => child.Text).ToList()));
            if (name == "BypassHide") command.BypassVisibility("Visible");
        }
        var child = builder.Entity<SavedBatchChild>();
        child.HasInMemoryStore<SavedBatchChildData, int>(row => row.Id, row => new(row.Id, row.TenantId, row.ParentId, row.Text, row.Hidden))
            .MapInMemoryEntity<SavedBatchChildData>(row => new SavedBatchChild { Id = row.Id, TenantId = row.TenantId, ParentId = row.ParentId, Text = row.Text, Hidden = row.Hidden })
            .MapInMemoryWrites<SavedBatchChildData>((row, saved) => new(row.Id, row.TenantId, row.ParentId, row.Text, row.Hidden));
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Text, "shared save")).Stage();
        return builder;
    }

    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Repositories()
    {
        const string ns = "Generated.SavedBatch.TestDoubles.";
        var fixture = Activator.CreateInstance(Generated.Value.GetType(ns + "SavedBatchContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ParentStore(data).SeedRange([new(1, 1, "a", "old", 1, false), new(2, 1, "b", "old", 1, false), new(1, 2, "hidden", "old", 1, true)]);
        ChildStore(data).SeedRange([new(1, 1, 1, "A", false), new(2, 2, 1, "B", false), new(3, 1, 1, "hidden", true)]);
        return (Activator.CreateInstance(Generated.Value.GetType(ns + "SavedBatchParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(Generated.Value.GetType(ns + "SavedBatchChildRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<SavedBatchData, (int, int)> ParentStore(InMemoryWriteDataContext data)
        => (InMemoryStore<SavedBatchData, (int, int)>)data.GetType().GetProperty("SavedBatchParentStore")!.GetValue(data)!;
    internal static InMemoryStore<SavedBatchChildData, int> ChildStore(InMemoryWriteDataContext data)
        => (InMemoryStore<SavedBatchChildData, int>)data.GetType().GetProperty("SavedBatchChildStore")!.GetValue(data)!;
}
