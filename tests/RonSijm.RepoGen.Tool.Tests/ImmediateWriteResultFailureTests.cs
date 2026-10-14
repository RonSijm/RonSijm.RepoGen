using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateWriteResultFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultConstructorFailureRollsBackTheOperationOnFixtureAndActualSqliteWithoutLosingEarlierStaging(bool delete)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ImmediateContext(new DbContextOptionsBuilder<ImmediateContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = Builder(mapped: true); var command = builder.Entity<ImmediateRow>().HasCommand("FailResult")
            .Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId).SimulateImmediateWrites();
        if (delete) command.Delete(); else command.Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 10));
        command.ReturnsMany(row => new ImmediateRejectedView(row.Id, row.Amount));
        var production = CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType("Generated.Immediate.Persistence.ImmediateRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (_, memory, data) = Fixture(mapped: true, assembly: Compile(builder));
        foreach (var repository in new[] { real, memory })
        {
            await Invoke<CommandResult>(repository, "StageCodeAsync", new ImmediateRequest(1, "staged", Delta: 1));
            Func<Task> fail = () => Invoke<MutationResult<ImmediateRejectedView>>(repository, "FailResultAsync", new ImmediateRequest(1));
            await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("returned value rejected");
        }
        MappedStore(data).Snapshot().Select(row => row.Amount).Should().Equal(2, 3, 4, 5);
        (await context.Set<ImmediateRow>().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Amount).ToArrayAsync()).Should().Equal(2, 3, 4, 5);
        data.PendingCount.Should().Be(1); (await data.SaveChangesAsync()).Should().Be(1); (await context.SaveChangesAsync()).Should().Be(1);
        MappedStore(data).Find(1)!.Code.Should().Be("staged");
    }

    [Fact]
    public async Task CompoundPublicWriteKeysAlsoSupportImmediateCountsWithoutEntityConstruction()
    {
        using var context = new ProxyWriteCompoundContext(new DbContextOptionsBuilder<ProxyWriteCompoundContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = ProxyWriteTestHarness.Builder(); var entity = builder.Entity<ProxyWriteCompoundRow>();
        entity.HasInMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>(row => new(row.Parent, row.Position), row => new(row.Parent, row.Position, row.Name))
            .MapInMemoryEntity<ProxyWriteCompoundData>(row => new ProxyWriteCompoundRow { ParentId = row.Parent, Slot = row.Position, Caption = row.Name })
            .MapInMemoryWrites<ProxyWriteCompoundData>((row, saved) => new(row.ParentId, row.Slot, row.Caption));
        entity.HasCommand("Rename").Where<ProxyWriteCompoundData>((row, request) => row.ParentId == request.Parent)
            .Update(setters => setters.SetProperty<string, ProxyWriteCompoundData>(row => row.Caption, (row, request) => request.Name)).SimulateImmediateWrites();
        entity.HasCommand("Clear").Where<ProxyWriteCompoundData>((row, request) => row.ParentId == request.Parent).Delete().SimulateImmediateWrites();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteCompoundContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.ProxyWrites.TestDoubles.ProxyWriteCompoundRowRepositoryTestDouble")!, fixture)!;
        var data = (RonSijm.RepoGen.Testing.InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (RonSijm.RepoGen.Testing.InMemoryStore<ProxyWriteCompoundData, ValueTuple<int, int>>)data.GetType().GetProperty("ProxyWriteCompoundRowStore")!.GetValue(data)!;
        store.SeedRange([new(1, 1, "first"), new(1, 2, "second"), new(2, 1, "other")]);
        (await ProxyWriteTestHarness.Invoke<int>(repository, "RenameAsync", new ProxyWriteCompoundData(1, 0, "renamed"))).Should().Be(2);
        store.Find((1, 2))!.Name.Should().Be("renamed"); store.Find((2, 1))!.Name.Should().Be("other");
        (await ProxyWriteTestHarness.Invoke<int>(repository, "ClearAsync", new ProxyWriteCompoundData(1, 0, ""))).Should().Be(2);
        store.Count.Should().Be(1); data.PendingCount.Should().Be(0);
    }
}
