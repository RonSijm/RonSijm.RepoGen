using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MemoryWriteTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryWriteRepositoryDesigner()));
    });
    internal static MemoryWriteContext Context() => new(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite("Data Source=:memory:").Options);
    internal static (object Note, object Audit, InMemoryWriteDataContext Data) Repositories()
    {
        var assembly = Generated.Value;
        const string ns = "Generated.MemoryWrites.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteContextRepositoryFixture")!)!;
        var note = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var audit = Activator.CreateInstance(assembly.GetType(ns + "MemoryWriteAuditRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        NoteStore(data).Seed(new() { Id = 1, Title = "old", Version = 1 });
        NoteStore(data).Seed(new() { Id = 2, Title = "hidden", Version = 1, Hidden = true });
        return (note, audit, data);
    }
    internal static InMemoryStore<MemoryWriteNote, int> NoteStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MemoryWriteNote, int>)data.GetType().GetProperty("MemoryWriteNoteStore")!.GetValue(data)!;
    internal static InMemoryStore<MemoryWriteAudit, int> AuditStore(InMemoryWriteDataContext data)
        => (InMemoryStore<MemoryWriteAudit, int>)data.GetType().GetProperty("MemoryWriteAuditStore")!.GetValue(data)!;
    internal static Task<TResult> Invoke<TResult>(object repository, string name, params object?[] inputs)
        => (Task<TResult>)repository.GetType().GetMethod(name)!.Invoke(repository, inputs.Append((object)CancellationToken.None).ToArray())!;
}
