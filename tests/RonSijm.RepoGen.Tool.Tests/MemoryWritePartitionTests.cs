using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryWritePartitionTests
{
    [Fact]
    public async Task RequestBoundContextKeysKeepPendingSavesAndSameKeysInSeparatePartitions()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.KeyWrites.Contracts")
            .UseGeneratedNamespace("Generated.KeyWrites.Persistence").UseScriptedTestDoubles("Generated.KeyWrites.TestDoubles")
            .UseKeyedContextLeases<string>().UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var note = builder.Entity<MemoryWriteNote>();
        note.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        note.HasCommand("Stage").Create<MemoryWriteRequest>(request => new MemoryWriteNote { Id = request.Id, Title = request.Title, Version = 1 })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).UseContextKey<MemoryWriteRequest, string>(request => request.ContextKey).Stage();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.KeyWrites.TestDoubles.MemoryWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.KeyWrites.TestDoubles.MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryWriteRequest(1, "A"));
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryWriteRequest(1, "B", ContextKey: "B"));
        first.PendingCount.Should().Be(1);
        second.PendingCount.Should().Be(1);
        await first.SaveChangesAsync();
        MemoryWriteTestHarness.NoteStore(first).Find(1)!.Title.Should().Be("A");
        MemoryWriteTestHarness.NoteStore(second).Count.Should().Be(0);
        second.DiscardChanges();
        second.PendingCount.Should().Be(0);
        MemoryWriteTestHarness.NoteStore(first).Count.Should().Be(1);
    }
}
