using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MemoryCollectionTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner()));
    });
    internal static MemoryCollectionContext Context() => new(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Repositories()
    {
        const string ns = "Generated.MemoryCollections.TestDoubles.";
        var assembly = Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionContextRepositoryFixture")!)!;
        var parent = Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionParentRepositoryTestDouble")!, fixture)!;
        var child = Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionChildRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ParentStore(data).SeedRange([new() { Id = 1, OwnerId = 10, Name = "old" }, new() { Id = 2, OwnerId = 20, Name = "other" }]);
        ChildStore(data).SeedRange([new() { Id = 1, ParentId = 1, Key = "a", Text = "old A" }, new() { Id = 2, ParentId = 1, Key = "b", Text = "old B" },
            new() { Id = 3, ParentId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new() { Id = 4, ParentId = 2, Key = "other", Text = "other" }]);
        return (parent, child, data);
    }
    internal static InMemoryStore<MemoryCollectionParent, int> ParentStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MemoryCollectionParent, int>)data.GetType().GetProperty("MemoryCollectionParentStore")!.GetValue(data)!;
    internal static InMemoryStore<MemoryCollectionChild, int> ChildStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MemoryCollectionChild, int>)data.GetType().GetProperty("MemoryCollectionChildStore")!.GetValue(data)!;
}
