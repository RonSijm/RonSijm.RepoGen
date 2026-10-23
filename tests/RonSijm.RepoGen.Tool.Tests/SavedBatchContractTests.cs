using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchContractTests
{
    [Fact]
    public async Task ActualGeneratedSqliteAndMappedFakeShareOrderedFreshCompositeKeyAndRelatedResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var capture = new SavedBatchCommandCapture();
        await using var context = new SavedBatchContext(new DbContextOptionsBuilder<SavedBatchContext>().UseSqlite(connection).AddInterceptors(capture).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new SavedBatchParent { TenantId = 1, Id = 1, Code = "a", Name = "old" }, new SavedBatchParent { TenantId = 2, Id = 1, Code = "b", Name = "old" },
            new SavedBatchParent { TenantId = 1, Id = 2, Code = "hidden", Name = "old", Hidden = true });
        context.AddRange(new SavedBatchChild { Id = 1, TenantId = 1, ParentId = 1, Text = "A" }, new SavedBatchChild { Id = 2, TenantId = 2, ParentId = 1, Text = "B" },
            new SavedBatchChild { Id = 3, TenantId = 1, ParentId = 1, Text = "hidden", Hidden = true });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedBatchTestHarness.Builder().Build());
        var realParent = Activator.CreateInstance(assembly.GetType("Generated.SavedBatch.Persistence.SavedBatchParentRepositoryOperations")!, context)!;
        var realChild = Activator.CreateInstance(assembly.GetType("Generated.SavedBatch.Persistence.SavedBatchChildRepositoryOperations")!, context)!;
        var (fakeParent, fakeChild, _) = SavedBatchTestHarness.Repositories();
        await Invoke<CommandResult>(realChild, "StageTextAsync", 1); await Invoke<CommandResult>(fakeChild, "StageTextAsync", 1); capture.Queries.Clear();
        var request = new SavedBatchRequest("shared", [new("b", "second"), new("a", "first")]);
        var real = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(realParent, "RefreshAsync", request);
        var fake = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(fakeParent, "RefreshAsync", request);
        JsonSerializer.Serialize(fake).Should().Be(JsonSerializer.Serialize(real));
        capture.Queries.Should().HaveCount(2, "there is one target load and one batch result read, not a result query per item");
        var values = real.Value!;
        values.Select(row => row.Code).Should().Equal("b", "a"); values[1].Children.Should().Equal("shared save");
        context.ChangeTracker.Entries<SavedBatchParent>().Should().OnlyContain(entry => !entry.Collection(row => row.Children).IsLoaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualProviderAndFakeRecheckRootVisibilityWithoutUndoingTheSavedBatch(bool bypass)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new SavedBatchContext(new DbContextOptionsBuilder<SavedBatchContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new SavedBatchParent { TenantId = 1, Id = 1, Code = "a", Name = "old" }, new SavedBatchParent { TenantId = 2, Id = 1, Code = "b", Name = "old" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedBatchTestHarness.Builder().Build());
        var real = Activator.CreateInstance(assembly.GetType("Generated.SavedBatch.Persistence.SavedBatchParentRepositoryOperations")!, context)!;
        var (fake, _, data) = SavedBatchTestHarness.Repositories();
        var request = new SavedBatchRequest("hide", [new("b", "saved"), new("a", "saved")]);
        if (bypass)
        {
            var realResult = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(real, "BypassHideAsync", request);
            var fakeResult = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(fake, "BypassHideAsync", request);
            realResult.Value!.Select(row => row.Code).Should().Equal(fakeResult.Value!.Select(row => row.Code));
        }
        else
            foreach (var repository in new[] { real, fake })
            {
                Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(repository, "HideAsync", request);
                await call.Should().ThrowAsync<RepositorySavedResultException>();
            }
        (await context.Set<SavedBatchParent>().AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Hidden && row.Name == "saved");
        SavedBatchTestHarness.ParentStore(data).Snapshot().Where(row => row.Code != "hidden").Should().OnlyContain(row => row.Hidden && row.Name == "saved");
    }

    [Fact]
    public async Task RealTransactionRollbackCanRestoreAResultFailureAfterTheSharedSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new SavedBatchContext(new DbContextOptionsBuilder<SavedBatchContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.Add(new SavedBatchParent { TenantId = 1, Id = 1, Code = "a", Name = "old" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedBatchTestHarness.Builder().Build());
        var real = Activator.CreateInstance(assembly.GetType("Generated.SavedBatch.Persistence.SavedBatchParentRepositoryOperations")!, context)!;
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<SavedResultRejectedView>>>(real, "FailAsync", new SavedBatchRequest("fail", [new("a", "saved")]));
            await call.Should().ThrowAsync<RepositorySavedResultException>();
            (await context.Set<SavedBatchParent>().AsNoTracking().SingleAsync()).Name.Should().Be("saved");
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); (await context.Set<SavedBatchParent>().SingleAsync()).Name.Should().Be("old");
    }
}
