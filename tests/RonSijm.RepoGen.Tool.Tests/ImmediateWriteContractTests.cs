using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateWriteContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameNamedScalarCommandsShareContractsWithRealSqliteIncludingRollbackAndPendingConflicts(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ImmediateContext(new DbContextOptionsBuilder<ImmediateContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType("Generated.Immediate.Persistence.ImmediateRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (_, memory, data) = Fixture(mapped);
        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var actual = await Invoke<TResult>(memory, name, inputs); var expected = await Invoke<TResult>(real, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new ImmediateRequest(1));
        await Equal<int>("CountUpdateAsync", new ImmediateRequest(1));
        await Equal<MutationResult<ImmediateView>>("BumpAsync", new ImmediateRequest(1, Delta: 5));
        await Equal<CommandResult>("CompareAndSetAsync", new ImmediateRequest(1, "one", ExpectedVersion: 1));
        await Equal<CommandResult>("CompareAndSetAsync", new ImmediateRequest(1, "one", ExpectedVersion: 2));
        await Equal<MutationResult<ImmediateView>>("ConsumeStampAsync", new ImmediateStampRequest(2, [3]));
        await Equal<MutationResult<ImmediateView>>("ConsumeAsync", new ImmediateRequest(1, "hidden"));
        await Equal<MutationResult<ImmediateView>>("ConsumeAsync", new ImmediateRequest(1, "one", ExpectedVersion: 1));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<MutationResult<ImmediateView>>("ConsumeAsync", new ImmediateRequest(1, "one", ExpectedVersion: 2));
            await transaction.RollbackAsync();
        }
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new ImmediateRequest(1));
        await Equal<CommandResult>("StageCodeAsync", new ImmediateRequest(1, "staged", Delta: 1));
        await Equal<MutationResult<ImmediateView>>("BumpAsync", new ImmediateRequest(1));
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new ImmediateRequest(1));
        data.PendingCount.Should().Be(1); context.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Modified).Should().Be(1);
        Func<Task> saveMemory = () => data.SaveChangesAsync(); await saveMemory.Should().ThrowAsync<RepositoryWriteConflictException>();
        Func<Task> saveReal = () => context.SaveChangesAsync(); await saveReal.Should().ThrowAsync<DbUpdateConcurrencyException>();
        data.DiscardChanges(); context.ChangeTracker.Clear();
        await Equal<int>("CountDeleteAsync", new ImmediateRequest(1, "one"));
        await Equal<int>("CountDeleteAsync", new ImmediateRequest(1, "one"));
        await Equal<IReadOnlyList<ImmediateView>>("ListAsync", new ImmediateRequest(1));
        (await context.Set<ImmediateRow>().CountAsync()).Should().Be(mapped ? MappedStore(data).Count : EntityStore(data).Count);
    }
}
