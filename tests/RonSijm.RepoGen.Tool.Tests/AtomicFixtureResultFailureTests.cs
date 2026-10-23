using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureResultFailureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReturnedConstructorFailureRollsBackInsertOrUpdateWhilePreservingEarlierStaging(bool mapped, bool update)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AtomicFixtureContext(new DbContextOptionsBuilder<AtomicFixtureContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = AtomicFixtureTestHarness.Builder(mapped, suppliedIds: true);
        builder.Entity<ImmediateRow>().HasCommand("Reject")
            .Create<AtomicFixtureInput>(request => new ImmediateRow { Id = request.Id, OwnerId = request.OwnerId, Code = request.Code,
                Amount = request.Amount, Previous = 0, Version = 1, Stamp = request.Stamp ?? new byte[] { 1 }, Hidden = false })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).AllowWrite(row => row.Stamp)
            .InsertOrUpdate(row => new { row.OwnerId, row.Code }, setters => setters
                .SetProperty<int, AtomicFixtureInput>(row => row.Amount, (row, request) => request.Amount))
            .Returns(row => new AtomicFixtureRejectedView(row.Id, row.Amount)).SimulateImmediateWrites();
        var assembly = AtomicFixtureTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(assembly.GetType("Generated.AtomicFixture.Persistence.ImmediateRowRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (_, memory, data) = AtomicFixtureTestHarness.Fixture(mapped, AtomicFixtureTestHarness.Compile(builder));
        foreach (var repository in new[] { real, memory })
        {
            await Invoke<CommandResult>(repository, "StageCodeAsync", new AtomicFixtureInput(1, "staged", Id: 1));
            Func<Task> fail = () => Invoke<CommandResult<AtomicFixtureRejectedView>>(repository, "RejectAsync",
                new AtomicFixtureInput(1, update ? "two" : "new", Amount: -1, Id: 99));
            await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("returned value rejected");
        }
        (await context.Set<ImmediateRow>().CountAsync()).Should().Be(4);
        (await Invoke<IReadOnlyList<ImmediateView>>(real, "ListAsync", new AtomicFixtureInput(1))).Select(row => row.Amount).Should().Equal(2, 3);
        (await Invoke<IReadOnlyList<ImmediateView>>(memory, "ListAsync", new AtomicFixtureInput(1))).Select(row => row.Amount).Should().Equal(2, 3);
        data.PendingCount.Should().Be(1); context.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Modified).Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); (await context.SaveChangesAsync()).Should().Be(1);
    }
}
