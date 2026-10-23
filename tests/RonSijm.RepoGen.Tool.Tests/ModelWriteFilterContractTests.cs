using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using Writes = RonSijm.RepoGen.Tool.Tests.ModelWriteFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterContractTests
{
    private static Task<T> Call<T>(object repository, string name, params object?[] arguments)
        => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", arguments);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedSqliteAndFixturesShareFilteredScalarWritesBatchesCreatesAndImmediateCounts(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Writes.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Writes.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Writes.Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context)!;
        var audit = Activator.CreateInstance(assembly.GetType(Writes.Namespace + ".Persistence.MemoryWriteAuditRepositoryOperations")!, context)!;
        var (fake, fakeAudit, data) = Writes.Fixture(mapped);
        async Task Equal<T>(string name, params object?[] input)
            => JsonSerializer.Serialize(await Call<T>(fake, name, input)).Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, input)), name);
        await Equal<CommandResult<ModelWriteFilterCard>>("Rename", new ModelWriteFilterPatch(2, "deleted", 99));
        await Equal<CommandResult<ModelWriteFilterCard>>("Rename", new ModelWriteFilterPatch(3, "hidden"));
        await Equal<CommandResult<ModelWriteFilterCard>>("Rename", new ModelWriteFilterPatch(1, "stale", 99));
        await Equal<CommandResult<ModelWriteFilterCard>>("RenameBypass", new ModelWriteFilterPatch(2, "deleted"));
        await Equal<CommandResult<ModelWriteFilterCard>>("RenameBypass", new ModelWriteFilterPatch(3, "shown")); context.ChangeTracker.Clear();
        await Equal<CommandResult<ModelWriteFilterCard>>("Rename", new ModelWriteFilterPatch(1, "fresh")); context.ChangeTracker.Clear();
        await Equal<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>("RenameMany", (object)new ModelWriteFilterPatch[] { new(4, "fourth"), new(1, "first", 1) });
        context.ChangeTracker.Clear();
        var invalid = new ModelWriteFilterPatch[] { new(1, "must-not-change", 2), new(2, "deleted") };
        await Equal<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>("RenameMany", (object)invalid);
        await Equal<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>("DeleteMany", (object)invalid); context.ChangeTracker.Clear();
        await Equal<CommandResult<ModelWriteFilterCard>>("CreateSaved", new ModelWriteFilterPatch(10, "ten")); context.ChangeTracker.Clear();
        await Equal<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>("CreateMany", (object)new ModelWriteFilterPatch[] { new(12, "twelve"), new(11, "eleven") });
        context.ChangeTracker.Clear();
        await Equal<CommandResult<ModelWriteFilterCard>>("Delete", new ModelWriteFilterPatch(10, "")); context.ChangeTracker.Clear();
        await Equal<CommandResult>("Update", new ModelWriteFilterPatch(2, "blocked"));
        await Equal<CommandResult>("Update", new ModelWriteFilterPatch(1, "stale", 99));
        await Equal<CommandResult>("StageRename", new ModelWriteFilterPatch(1, "pending", 2));
        JsonSerializer.Serialize(await Call<CommandResult>(fakeAudit, "StageAudit", new ModelWriteFilterPatch(50, "shared")))
            .Should().Be(JsonSerializer.Serialize(await Call<CommandResult>(audit, "StageAudit", new ModelWriteFilterPatch(50, "shared"))));
        await Equal<CommandResult>("UpdateAll"); data.PendingCount.Should().Be(2);
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); context.ChangeTracker.Clear();
        await Equal<IReadOnlyList<ModelWriteFilterCard>>("Cards");
        await Equal<CommandResult<string>>("Hide", 4); context.ChangeTracker.Clear();
        await Equal<CommandResult>("ExecuteDelete", new ModelWriteFilterPatch(4, "", 1));
        await Equal<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>("DeleteMany", (object)new ModelWriteFilterPatch[] { new(12, ""), new(11, "") });
        context.ChangeTracker.Clear(); await Equal<IReadOnlyList<ModelWriteFilterCard>>("All");
        (await context.Set<MemoryWriteAudit>().AsNoTracking().SingleAsync()).Text.Should().Be("shared");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StagedSoftDeleteStillAllowsSavedRowSelectionThenSharedSave(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Writes.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Writes.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Writes.Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context)!;
        var (fake, _, data) = Writes.Fixture(mapped);
        foreach (var repository in new[] { fake, real })
        {
            (await Call<CommandResult>(repository, "StageHide", 1)).Status.Should().Be(CommandStatus.Staged);
            (await Call<CommandResult>(repository, "StageRename", new ModelWriteFilterPatch(1, "still selectable"))).Status.Should().Be(CommandStatus.Staged);
        }
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); context.ChangeTracker.Clear();
        var actual = await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == 1);
        Writes.Rows(data, mapped)[0].Should().Be(new ModelWriteFilterSeed(actual.Id, actual.Title, actual.Version, actual.Deleted, actual.Hidden));
        actual.Deleted.Should().BeTrue(); actual.Title.Should().Be("still selectable");
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterCard>>(fake, "Cards"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelWriteFilterCard>>(real, "Cards")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilteredSavedResultFailureOccursAfterWholeSaveAndRollbackRemainsExplicit(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelWriteFilterContext(new DbContextOptionsBuilder<ModelWriteFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Writes.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Writes.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Writes.Namespace + ".Persistence.ModelWriteFilterNoteRepositoryOperations")!, context)!;
        var (fake, _, data) = Writes.Fixture(mapped);
        foreach (var repository in new[] { fake, real })
        {
            (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Create", new ModelWriteFilterPatch(10, "deleted insert", Deleted: true))).Succeeded.Should().BeTrue();
            Func<Task> hide = async () => await Call<CommandResult<ModelWriteFilterCard>>(repository, "HideAndRead", 1);
            await hide.Should().ThrowAsync<RepositorySavedResultException>();
        }
        context.ChangeTracker.Clear();
        (await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == 1)).Deleted.Should().BeTrue();
        Writes.Rows(data, mapped)[0].Deleted.Should().BeTrue();
        await using var databaseTransaction = await context.Database.BeginTransactionAsync();
        await using var fixtureTransaction = await data.BeginTransactionAsync();
        foreach (var repository in new[] { fake, real })
        {
            var inputs = new ModelWriteFilterPatch[] { new(11, "visible"), new(12, "deleted", Deleted: true) };
            Func<Task> create = async () => await Call<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>(repository, "CreateMany", (object)inputs);
            await create.Should().ThrowAsync<RepositorySavedResultException>();
        }
        (await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().CountAsync()).Should().Be(7); Writes.Rows(data, mapped).Should().HaveCount(7);
        await databaseTransaction.RollbackAsync(); await fixtureTransaction.RollbackAsync(); context.ChangeTracker.Clear();
        (await context.Set<ModelWriteFilterNote>().IgnoreQueryFilters().CountAsync()).Should().Be(5); Writes.Rows(data, mapped).Should().HaveCount(5);
    }
}
