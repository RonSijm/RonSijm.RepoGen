using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualSqliteAndFixtureReturnTheSameMatchedUpdatedAndDeletedValues(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelWriteFilterTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (fake, data) = Fixture(mapped);
        async Task Equal(string name, params object?[] inputs)
        {
            var actual = await Call<MutationResult<ModelWriteFilterCard>>(fake, name, inputs);
            var expected = await Call<MutationResult<ModelWriteFilterCard>>(real, name, inputs);
            actual.AffectedRows.Should().Be(expected.AffectedRows);
            actual.Items.OrderBy(row => row.Id).Should().Equal(expected.Items.OrderBy(row => row.Id), name);
        }
        await Equal("Update", new ModelWriteFilterPatch(2, "deleted")); await Equal("Update", new ModelWriteFilterPatch(3, "hidden"));
        await Equal("Update", new ModelWriteFilterPatch(1, "stale", 99));
        await Equal("UpdateBypass", new ModelWriteFilterPatch(3, "shown"));
        await Equal("ConsumeBypass", new ModelWriteFilterPatch(2, "blocked"));
        await Equal("Consume", new ModelWriteFilterPatch(1, "stale", 99));
        await Equal("ConsumeBypass", new ModelWriteFilterPatch(3, "", 1));
        await Equal("Update", new ModelWriteFilterPatch(1, "fresh"));
        await Equal("ArchiveAll"); await Equal("ArchiveAll");
        (await Call<IReadOnlyList<ModelWriteFilterCard>>(fake, "Cards")).Should().BeEmpty();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All")).Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(real, "All")));
        // Make two valid unfiltered roots visible again through explicit test setup, then consume them.
        await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().Where(row => row.Id == 1 || row.Id == 4).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Deleted, false));
        context.ChangeTracker.Clear();
        foreach (var row in ModelWriteFilterTestHarness.Rows(data, mapped).Where(row => row.Id is 1 or 4))
            if (mapped) ModelWriteFilterTestHarness.Store<ModelWriteFilterSeed>(data).Seed(row with { Deleted = false }, RonSijm.RepoGen.Testing.InMemoryDuplicateKeyBehavior.Replace);
            else ModelWriteFilterTestHarness.Store<ModelWriteFilterNote>(data).Seed(new() { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = false, Hidden = row.Hidden }, RonSijm.RepoGen.Testing.InMemoryDuplicateKeyBehavior.Replace);
        await Equal("ConsumeAll"); await Equal("ConsumeAll");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingStateVersionConflictsUnfilteredSharedSaveAndRollbackMatchSqlite(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelWriteFilterTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (fake, data) = Fixture(mapped);
        foreach (var repository in new[] { fake, real })
        {
            await Call<CommandResult>(repository, "StageTitle", new ModelWriteFilterPatch(1, "pending"));
            (await Call<MutationResult<ModelWriteFilterCard>>(repository, "Update", new ModelWriteFilterPatch(1, "native"))).Items.Single().Title.Should().Be("native");
        }
        data.PendingCount.Should().Be(1); context.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Modified).Should().Be(1);
        Func<Task> fixtureSave = () => data.SaveChangesAsync(); await fixtureSave.Should().ThrowAsync<RepositoryWriteConflictException>();
        Func<Task> databaseSave = () => context.SaveChangesAsync(); await databaseSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        data.DiscardChanges(); context.ChangeTracker.Clear();
        foreach (var repository in new[] { fake, real })
        {
            await Call<CommandResult>(repository, "StageTitle", new ModelWriteFilterPatch(1, "pending again", 1));
            (await Call<MutationResult<ModelWriteFilterCard>>(repository, "Mark", 1)).Items.Single().Deleted.Should().BeTrue();
        }
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All")).Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(real, "All")));
        await using var databaseTransaction = await context.Database.BeginTransactionAsync();
        await using var fixtureTransaction = await data.BeginTransactionAsync();
        foreach (var repository in new[] { fake, real }) (await Call<MutationResult<ModelWriteFilterCard>>(repository, "ConsumeAll")).AffectedRows.Should().Be(1);
        await databaseTransaction.RollbackAsync(); await fixtureTransaction.RollbackAsync(); context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(fake, "All")).Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterSeed>>(real, "All")));
    }
}
