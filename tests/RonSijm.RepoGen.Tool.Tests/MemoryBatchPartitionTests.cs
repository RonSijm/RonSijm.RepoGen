using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchPartitionTests
{
    [Fact]
    public async Task RequestBoundBatchesStageAndSaveOnlyTheirSelectedContextPartition()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.KeyBatches.Contracts")
            .UseGeneratedNamespace("Generated.KeyBatches.Persistence").UseScriptedTestDoubles("Generated.KeyBatches.TestDoubles")
            .UseKeyedContextLeases<string>().UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var note = builder.Entity<MemoryWriteNote>();
        note.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        note.HasCommand("Stage").PatchMany<MemoryWriteRequest, int, MemoryBatchRequest>(request => request.Items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, (row, item) => item.Title), maximumItems: 2)
            .UseContextKey<MemoryBatchRequest, string>(request => request.ContextKey).Stage();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.KeyBatches.TestDoubles.MemoryWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.KeyBatches.TestDoubles.MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        MemoryWriteTestHarness.NoteStore(first).Seed(new() { Id = 1, Title = "old A", Version = 1 });
        MemoryWriteTestHarness.NoteStore(second).Seed(new() { Id = 1, Title = "old B", Version = 1 });
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryBatchRequest([new(1, "A")], "A"));
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryBatchRequest([new(1, "B")], "B"));
        first.PendingCount.Should().Be(1); second.PendingCount.Should().Be(1);
        (await first.SaveChangesAsync()).Should().Be(1);
        MemoryWriteTestHarness.NoteStore(first).Find(1)!.Title.Should().Be("A");
        MemoryWriteTestHarness.NoteStore(second).Find(1)!.Title.Should().Be("old B");
        second.PendingCount.Should().Be(1);
        (await second.SaveChangesAsync()).Should().Be(1);
        MemoryWriteTestHarness.NoteStore(second).Find(1)!.Title.Should().Be("B");
    }
}
