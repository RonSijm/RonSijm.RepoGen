using System.Reflection;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MemoryBatchTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = MemoryWriteTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryBatchRepositoryDesigner()));
    });
    internal static (object Note, object Audit, InMemoryWriteDataContext Data) Repositories()
    {
        var assembly = Generated.Value;
        const string ns = "Generated.MemoryBatches.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteContextRepositoryFixture")!)!;
        var note = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var audit = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteAuditRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        MemoryWriteTestHarness.NoteStore(data).SeedRange([new() { Id = 1, Title = "one", Version = 1 }, new() { Id = 2, Title = "two", Version = 1 }, new() { Id = 3, Title = "hidden", Version = 1, Hidden = true }]);
        return (note, audit, data);
    }
}
