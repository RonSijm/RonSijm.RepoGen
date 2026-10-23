using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyWriteCompoundTests
{
    [Fact]
    public async Task CompoundPrimaryKeysUseExplicitSameTypeAliasesForCreatePatchAndDelete()
    {
        using var context = new ProxyWriteCompoundContext(new DbContextOptionsBuilder<ProxyWriteCompoundContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(); var entity = builder.Entity<ProxyWriteCompoundRow>();
        entity.HasInMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>(row => new(row.Parent, row.Position), row => new(row.Parent, row.Position, row.Name))
            .MapInMemoryEntity<ProxyWriteCompoundData>(row => new ProxyWriteCompoundRow { ParentId = row.Parent, Slot = row.Position, Caption = row.Name })
            .MapInMemoryWrites<ProxyWriteCompoundData>((row, saved) => new(row.ParentId, row.Slot, row.Caption));
        entity.HasCommand("Add").Create<ProxyWriteCompoundData>(request => new ProxyWriteCompoundRow { ParentId = request.Parent, Slot = request.Position, Caption = request.Name })
            .AllowWrite(row => row.ParentId).AllowWrite(row => row.Slot).Save();
        entity.HasCommand("Patch").Where<ProxyWriteCompoundData>((row, request) => row.ParentId == request.Parent && row.Slot == request.Position)
            .Patch(setters => setters.SetProperty<string, ProxyWriteCompoundData>(row => row.Caption, (row, request) => request.Name)).Stage();
        entity.HasCommand("Remove").Where<ProxyWriteCompoundData>((row, request) => row.ParentId == request.Parent && row.Slot == request.Position).TrackedDelete().Save();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteCompoundContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteCompoundRowRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>)data.GetType().GetProperty("ProxyWriteCompoundRowStore")!.GetValue(data)!;
        store.Seed(new(3, 4, "old"));
        (await Invoke<CommandResult>(repository, "AddAsync", new ProxyWriteCompoundData(3, 5, "new"))).Status.Should().Be(CommandStatus.Applied);
        await Invoke<CommandResult>(repository, "PatchAsync", new ProxyWriteCompoundData(3, 4, "changed"));
        store.Find((3, 4))!.Name.Should().Be("old"); await data.SaveChangesAsync(); store.Find((3, 4))!.Name.Should().Be("changed");
        await Invoke<CommandResult>(repository, "RemoveAsync", new ProxyWriteCompoundData(3, 5, "")); store.Find((3, 5)).Should().BeNull();
        entity.HasInMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>(row => new(row.Position, row.Parent), row => new(row.Parent, row.Position, row.Name))
            .MapInMemoryEntity<ProxyWriteCompoundData>(row => new ProxyWriteCompoundRow { ParentId = row.Parent, Slot = row.Position, Caption = row.Name })
            .MapInMemoryWrites<ProxyWriteCompoundData>((row, saved) => new(row.ParentId, row.Slot, row.Caption));
        Action reordered = () => RepositoryGenerator.Generate(context, builder.Build()); reordered.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*");
    }
}
