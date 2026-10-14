using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultContractTests
{
    [Fact]
    public async Task ActualGeneratedSqliteAndFakeReturnTheSameFreshGraphForReconciliationScalarPatchAndCreate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryCollectionParent { Id = 1, OwnerId = 10, Name = "old" }, new MemoryCollectionParent { Id = 2, OwnerId = 20, Name = "other" });
        context.AddRange(new MemoryCollectionChild { Id = 1, ParentId = 1, Key = "a", Text = "A" }, new MemoryCollectionChild { Id = 2, ParentId = 1, Key = "b", Text = "B" },
            new MemoryCollectionChild { Id = 3, ParentId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new MemoryCollectionChild { Id = 4, ParentId = 2, Key = "other", Text = "other" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedResultTestHarness.Builder().Build());
        var realParent = Activator.CreateInstance(assembly.GetType("Generated.SavedResults.Persistence.MemoryCollectionParentRepositoryOperations")!, context)!;
        var realChild = Activator.CreateInstance(assembly.GetType("Generated.SavedResults.Persistence.MemoryCollectionChildRepositoryOperations")!, context)!;
        var (fakeParent, fakeChild, _) = SavedResultTestHarness.Repositories();
        async Task Equal<TResult>(object real, object fake, string method, params object?[] inputs)
        {
            JsonSerializer.Serialize(await Invoke<TResult>(fake, method, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<TResult>(real, method, inputs)), method);
        }
        await Equal<CommandResult<SavedCollectionView>>(realParent, fakeParent, "RenameAsync", new MemoryCollectionRequest(1, 10, "renamed", []));
        await Equal<CommandResult<SavedCollectionView>>(realParent, fakeParent, "SaveGraphAsync", new MemoryCollectionRequest(1, 10, "saved", [new("a", "updated"), new("new", "added")], 2));
        await Equal<CommandResult<string>>(realChild, fakeChild, "CreateChildAsync", new MemoryCollectionRequest(1, 10, "created", []));
        await Equal<CommandResult>(realChild, fakeChild, "StageTextAsync", 1);
        await Equal<CommandResult<SavedCollectionView>>(realParent, fakeParent, "RenameAsync", new MemoryCollectionRequest(1, 10, "shared save", [], 3));
        await Equal<CommandResult<SavedCollectionView>>(realParent, fakeParent, "RenameAsync", new MemoryCollectionRequest(1, 99, "wrong owner", []));
        await Equal<CommandResult<string>>(realChild, fakeChild, "BypassHideAsync", 4);
        // No result read fixes up navigation graphs on the real context's tracked parent.
        context.Entry(await context.Set<MemoryCollectionParent>().SingleAsync(row => row.Id == 1)).Collection(row => row.Children).IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task ActualProviderResultFailuresLeaveTheWriteSavedAndExplicitTransactionRollbackStillWorks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.Add(new MemoryCollectionParent { Id = 1, Name = "old" }); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedResultTestHarness.Builder().Build());
        var parent = Activator.CreateInstance(assembly.GetType("Generated.SavedResults.Persistence.MemoryCollectionParentRepositoryOperations")!, context)!;
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Func<Task> execute = async () => await Invoke<CommandResult<SavedResultRejectedView>>(parent, "FailResultAsync", 1);
            await execute.Should().ThrowAsync<RepositorySavedResultException>();
            (await context.Set<MemoryCollectionParent>().AsNoTracking().SingleAsync()).Name.Should().Be("committed");
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); (await context.Set<MemoryCollectionParent>().SingleAsync()).Name.Should().Be("old");
    }
}
