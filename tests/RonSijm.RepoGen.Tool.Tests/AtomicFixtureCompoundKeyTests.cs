using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureCompoundKeyTests
{
    [Fact]
    public async Task ExplicitCompoundPrimaryConflictKeysWorkWithInternalModelsAndPublicScalarMaps()
    {
        using var context = new ProxyWriteCompoundContext(new DbContextOptionsBuilder<ProxyWriteCompoundContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.AtomicCompound.Contracts").UseAtomicWrites<SqliteAtomicWriter>()
            .UseScriptedTestDoubles("Generated.AtomicCompound.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<ProxyWriteCompoundRow>();
        entity.HasInMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>(row => new(row.Parent, row.Position), row => new(row.Parent, row.Position, row.Name))
            .MapInMemoryEntity<ProxyWriteCompoundData>(row => new ProxyWriteCompoundRow { ParentId = row.Parent, Slot = row.Position, Caption = row.Name })
            .MapInMemoryWrites<ProxyWriteCompoundData>((row, saved) => new(row.ParentId, row.Slot, row.Caption));
        entity.HasCommand("Ensure").Create<ProxyWriteCompoundData>(request => new ProxyWriteCompoundRow { ParentId = request.Parent, Slot = request.Position, Caption = request.Name })
            .AllowWrite(row => row.ParentId).AllowWrite(row => row.Slot).InsertIfAbsent(row => new { row.ParentId, row.Slot })
            .Returns(row => row.Caption).SimulateImmediateWrites();
        entity.HasCommand("Put").Create<ProxyWriteCompoundData>(request => new ProxyWriteCompoundRow { ParentId = request.Parent, Slot = request.Position, Caption = request.Name })
            .AllowWrite(row => row.ParentId).AllowWrite(row => row.Slot)
            .InsertOrUpdate(row => new { row.ParentId, row.Slot }, setters => setters.SetProperty<string, ProxyWriteCompoundData>(row => row.Caption, (row, request) => request.Name))
            .Returns(row => row.Caption).SimulateImmediateWrites();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.AtomicCompound.TestDoubles.ProxyWriteCompoundContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicCompound.TestDoubles.ProxyWriteCompoundRowRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>)data.GetType().GetProperty("ProxyWriteCompoundRowStore")!.GetValue(data)!;
        store.SeedRange([new(1, 1, "first"), new(1, 2, "second"), new(2, 1, "other")]);
        (await ImmediateTestHarness.Invoke<CommandResult<string>>(repository, "EnsureAsync", new ProxyWriteCompoundData(1, 3, "third"))).Status.Should().Be(CommandStatus.Inserted);
        (await ImmediateTestHarness.Invoke<CommandResult<string>>(repository, "EnsureAsync", new ProxyWriteCompoundData(1, 1, "ignored"))).Status.Should().Be(CommandStatus.AlreadyExists);
        var updated = await ImmediateTestHarness.Invoke<CommandResult<string>>(repository, "PutAsync", new ProxyWriteCompoundData(1, 2, "changed"));
        updated.Status.Should().Be(CommandStatus.Updated); updated.Value.Should().Be("changed");
        store.Find((1, 1))!.Name.Should().Be("first"); store.Find((2, 1))!.Name.Should().Be("other");
        store.Count.Should().Be(4); data.PendingCount.Should().Be(0);
    }
}
