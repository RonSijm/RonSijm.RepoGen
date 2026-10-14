using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class CreateManyTestHarness
{
    private static readonly Lazy<Assembly> EntityAssembly = new(() => Compile(false));
    private static readonly Lazy<Assembly> MappedAssembly = new(() => Compile(true));
    internal static CreateManyContext Context() => new(new DbContextOptionsBuilder<CreateManyContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new CreateManyRepositoryDesigner(mapped)));
    }
    internal static (object Repository, object Parent, InMemoryWriteDataContext Data) Fixture(bool mapped = false)
    {
        var assembly = mapped ? MappedAssembly.Value : EntityAssembly.Value;
        const string ns = "Generated.CreateMany.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "CreateManyContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ParentStore(data).Seed(new() { Id = 7, Name = "old" });
        if (mapped) DataStore(data).Seed(new(7, 7, "old", "fixture", false));
        else EntityStore(data).Seed(new() { Id = 7, ParentId = 7, Name = "old", Label = "fixture" });
        return (Activator.CreateInstance(assembly.GetType(ns + "CreateManyRowRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + "CreateManyParentRepositoryTestDouble")!, fixture)!, data);
    }
    internal static InMemoryStore<CreateManyRow, int> EntityStore(InMemoryWriteDataContext data)
        => (InMemoryStore<CreateManyRow, int>)data.GetType().GetProperty("CreateManyRowStore")!.GetValue(data)!;
    internal static InMemoryStore<CreateManyData, int> DataStore(InMemoryWriteDataContext data)
        => (InMemoryStore<CreateManyData, int>)data.GetType().GetProperty("CreateManyRowStore")!.GetValue(data)!;
    internal static InMemoryStore<CreateManyParent, int> ParentStore(InMemoryWriteDataContext data)
        => (InMemoryStore<CreateManyParent, int>)data.GetType().GetProperty("CreateManyParentStore")!.GetValue(data)!;
    internal static IReadOnlyList<CreateManyData> Snapshot(InMemoryWriteDataContext data, bool mapped)
        => mapped ? DataStore(data).Snapshot() : EntityStore(data).Snapshot().Select(row => new CreateManyData(row.Id, row.ParentId, row.Name, row.Label, row.Hidden)).ToArray();
}
