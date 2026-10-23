using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFilterContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedSqliteAndFixturesShareUnfilteredConflictsInsertsAndFilteredUpdateResults(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await SeedSqlite(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (fake, _, _) = Fixture(mapped);
        async Task Equal<T>(string name, params object?[] input)
            => JsonSerializer.Serialize(await Call<T>(fake, name, input)).Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, input)), name);
        await Equal<CommandResult<ModelWriteFilterCard>>("Ensure", new AtomicFilterInput(2));
        await Equal<CommandResult<ModelWriteFilterCard>>("Ensure", new AtomicFilterInput(10, "hidden insert", Deleted: true, Hidden: true));
        await Equal<CommandResult<ModelWriteFilterCard>>("Put", new AtomicFilterInput(11, "filtered insert", Deleted: true));
        await Equal<CommandResult<ModelWriteFilterCard>>("Put", new AtomicFilterInput(2));
        await Equal<CommandResult<ModelWriteFilterCard>>("Put", new AtomicFilterInput(3));
        await Equal<CommandResult<ModelWriteFilterCard>>("PutBypass", new AtomicFilterInput(2));
        await Equal<CommandResult<ModelWriteFilterCard>>("PutBypass", new AtomicFilterInput(3, "bypass"));
        await Equal<CommandResult<ModelWriteFilterCard>>("Put", new AtomicFilterInput(1, "stale", 99));
        await Equal<CommandResult<ModelWriteFilterCard>>("HidePut", new AtomicFilterInput(1, "returned despite visibility"));
        await Equal<CommandResult>("PutCount", new AtomicFilterInput(4, "counted"));
        await Equal<IReadOnlyList<ModelWriteFilterSeed>>("Cards"); await Equal<IReadOnlyList<ModelWriteFilterSeed>>("All");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ModelFilterChangingUpsertsFailEvenWithoutAConfiguredResultAndOwnedSqlTransactionsRollback(bool mapped, bool countOnly)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await SeedSqlite(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (fake, _, _) = Fixture(mapped);
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> fail = async () =>
            {
                if (countOnly) await Call<CommandResult>(repository, "ArchiveCount", new AtomicFilterInput(1, "must roll back"));
                else await Call<CommandResult<ModelWriteFilterCard>>(repository, "ArchivePut", new AtomicFilterInput(1, "must roll back"));
            };
            await fail.Should().ThrowAsync<InvalidOperationException>();
        }
        context.Database.CurrentTransaction.Should().BeNull();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(real, "All")));
        (await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().SingleAsync(row => row.Id == 1)).Deleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerOwnedSqlTransactionsMayNeedRollbackWhileFixtureFailuresStayPreCommit(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await SeedSqlite(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (fake, data, _) = Fixture(mapped);
        await using var sqlTransaction = await context.Database.BeginTransactionAsync();
        await using var fixtureTransaction = await data.BeginTransactionAsync();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> fail = async () => await Call<CommandResult<ModelWriteFilterCard>>(repository, "ArchivePut", new AtomicFilterInput(1, "requires rollback"));
            await fail.Should().ThrowAsync<InvalidOperationException>();
        }
        (await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == 1)).Deleted.Should().BeTrue();
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All")).Single(row => row.Id == 1).Deleted.Should().BeFalse();
        await sqlTransaction.RollbackAsync(); await fixtureTransaction.RollbackAsync();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(real, "All")));
    }

    private static async Task SeedSqlite(ModelWriteFilterContext context)
    {
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelWriteFilterTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
