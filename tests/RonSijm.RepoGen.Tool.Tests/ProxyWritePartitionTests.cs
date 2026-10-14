using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy writes")]
public sealed class ProxyWritePartitionTests
{
    [Fact]
    public async Task MappedWritesAndGeneratedReservationsAreIsolatedByRequestBoundContextKey()
    {
        using var context = Context(); var builder = Builder(); builder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = ProxyWriteRepositoryDesigner.Store(builder);
        entity.HasCommand("Stage").Create<MemoryWriteRequest>(request => new ProxyWriteRecord
            { Id = request.Id, Name = request.Title, Hidden = false, Label = null!, Stamp = Array.Empty<byte>() })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Label).UseContextKey<MemoryWriteRequest, string>(request => request.ContextKey).Stage();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteRecordRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        static InMemoryStore<ProxyWriteData, int> Store(InMemoryWriteDataContext data) => (InMemoryStore<ProxyWriteData, int>)data.GetType().GetProperty("ProxyWriteRecordStore")!.GetValue(data)!;
        foreach (var data in new[] { first, second }) Store(data).Seed(new(7, "seed", 4, "fixture", false, []));
        await Invoke<CommandResult>(repository, "StageAsync", new MemoryWriteRequest(0, "A"));
        await Invoke<CommandResult>(repository, "StageAsync", new MemoryWriteRequest(0, "A2"));
        await Invoke<CommandResult>(repository, "StageAsync", new MemoryWriteRequest(0, "B", ContextKey: "B"));
        first.PendingCount.Should().Be(2); second.PendingCount.Should().Be(1);
        await first.SaveChangesAsync(); Store(first).Find(8)!.Caption.Should().Be("A"); Store(first).Find(9)!.Caption.Should().Be("A2");
        Store(second).Count.Should().Be(1); await second.SaveChangesAsync(); Store(second).Find(8)!.Caption.Should().Be("B");
        Store(second).Find(9).Should().BeNull();
    }
}
