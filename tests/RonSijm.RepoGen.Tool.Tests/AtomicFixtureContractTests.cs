using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFixtureTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameDesignedInsertAndUpsertContractsMatchActualGeneratedSqliteOperations(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AtomicFixtureContext(new DbContextOptionsBuilder<AtomicFixtureContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        // Supplied IDs make the portable contract comparison exact. A fixture Max + 1 factory does
        // not emulate SQLite AUTOINCREMENT, which can reserve IDs even on conflict/no-op attempts.
        var builder = AtomicFixtureTestHarness.Builder(mapped, suppliedIds: true);
        var assembly = AtomicFixtureTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(assembly.GetType("Generated.AtomicFixture.Persistence.ImmediateRowRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (_, memory, data) = AtomicFixtureTestHarness.Fixture(mapped, AtomicFixtureTestHarness.Compile(builder));
        async Task Equal<TResult>(string name, AtomicFixtureInput request)
        {
            var actual = await Invoke<TResult>(memory, name, request); var expected = await Invoke<TResult>(real, name, request);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<CommandResult<ImmediateView>>("EnsureAsync", new(1, Id: 5));
        await Equal<CommandResult<ImmediateView>>("EnsureAsync", new(1, Id: 99));
        await Equal<CommandResult<ImmediateView>>("EnsureAsync", new(1, "hidden", Id: 99));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "hidden", Stamp: [4], Id: 99));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", Minimum: 99, Stamp: [1, 2], Id: 99));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", Stamp: [9], Id: 99));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", Stamp: [1, 2], Id: 99));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", Stamp: [1, 2], Id: 99));
        await Equal<CommandResult>("EnsureCountAsync", new(2, "counter", Id: 6));
        await Equal<CommandResult>("EnsureCountAsync", new(2, "counter", Id: 99));
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new(1));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<ImmediateView>>("EnsureAsync", new(1, "rollback", Id: 7));
            await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", ExpectedVersion: 2, Stamp: [1, 2], Id: 99));
            await transaction.RollbackAsync();
        }
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new(1));
        await Equal<CommandResult>("StageCodeAsync", new(1, "staged", Id: 1));
        await Equal<CommandResult<ImmediateView>>("UpsertAsync", new(1, "one", ExpectedVersion: 2, Stamp: [1, 2], Id: 99));
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new(1));
        data.PendingCount.Should().Be(1); context.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Modified).Should().Be(1);
        Func<Task> memorySave = () => data.SaveChangesAsync(); await memorySave.Should().ThrowAsync<RepositoryWriteConflictException>();
        Func<Task> realSave = () => context.SaveChangesAsync(); await realSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
