using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SavedGraphFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedGraphFilterContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedSqliteAndFixturesMatchFreshFilteredSavedGraphsAndSharedSaves(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelFilterContext(new DbContextOptionsBuilder<ModelFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelFilterTestHarness.Folders()); context.AddRange(ModelFilterTestHarness.Seeds());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedGraphFilterTestHarness.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, context)!;
        var realFolders = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterFolderRepositoryOperations")!, context)!;
        var (fake, fakeFolders, data) = Fixture(mapped);
        async Task Equal<T>(string name, params object?[] input)
            => JsonSerializer.Serialize(await Call<T>(fake, name, input)).Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, input)), name);
        await Equal<CommandResult<FilteredNavigationCard>>("Rename", new SavedGraphFilterRequest(2, "blocked"));
        await Equal<CommandResult<FilteredNavigationCard>>("Rename", new SavedGraphFilterRequest(3, "blocked"));
        await Equal<CommandResult<FilteredNavigationCard>>("Rename", new SavedGraphFilterRequest(1, "renamed")); context.ChangeTracker.Clear();
        await Equal<CommandResult<SavedGraphFilterCollectionCard>>("RenameGraph", new SavedGraphFilterRequest(1, "collection")); context.ChangeTracker.Clear();
        await Equal<CommandResult<FilteredNavigationCard>>("Rename", new SavedGraphFilterRequest(4, "archived target")); context.ChangeTracker.Clear();
        await Equal<CommandResult<FilteredNavigationCard>>("RenameBypass", new SavedGraphFilterRequest(3, "visible")); context.ChangeTracker.Clear();
        await Equal<CommandResult<IReadOnlyList<FilteredNavigationCard>>>("RenameMany", (object)new SavedGraphFilterRequest[] { new(5, "fifth"), new(1, "first"), new(4, "fourth") }); context.ChangeTracker.Clear();
        await Equal<CommandResult<FilteredNavigationCard>>("Create", new SavedGraphFilterRequest(9, "nine", 2)); context.ChangeTracker.Clear();
        await Equal<CommandResult<IReadOnlyList<FilteredNavigationCard>>>("CreateMany", (object)new SavedGraphFilterRequest[] { new(8, "eight", 2), new(7, "seven", 1) }); context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<CommandResult>(fakeFolders, "StageArchive", 1)).Should().Be(JsonSerializer.Serialize(await Call<CommandResult>(realFolders, "StageArchive", 1)));
        await Equal<CommandResult<SavedGraphFilterCollectionCard>>("RenameGraph", new SavedGraphFilterRequest(1, "shared")); context.ChangeTracker.Clear(); data.PendingCount.Should().Be(0);
        await Equal<IReadOnlyList<ModelFilterRowSeed>>("All");
        // Owner insertion uses the same fresh collection renderer, without persisting a child graph.
        JsonSerializer.Serialize(await Call<CommandResult<FilteredNavigationFolderCard>>(fakeFolders, "Create", new SavedGraphFilterFolderInput(6)))
            .Should().Be(JsonSerializer.Serialize(await Call<CommandResult<FilteredNavigationFolderCard>>(realFolders, "Create", new SavedGraphFilterFolderInput(6))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedGraphFailuresArePostSaveAndBatchTransactionsRemainExplicit(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelFilterContext(new DbContextOptionsBuilder<ModelFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelFilterTestHarness.Folders()); context.AddRange(ModelFilterTestHarness.Seeds());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, SavedGraphFilterTestHarness.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, context)!;
        var (fake, _, data) = Fixture(mapped);
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> archive = async () => await Call<CommandResult<FilteredNavigationCard>>(repository, "Archive", 1);
            await archive.Should().ThrowAsync<RepositorySavedResultException>();
        }
        context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelFilterRowSeed>>(fake, "All")).Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelFilterRowSeed>>(real, "All")));
        await using var databaseTransaction = await context.Database.BeginTransactionAsync();
        await using var fixtureTransaction = await data.BeginTransactionAsync();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> create = async () => await Call<CommandResult<IReadOnlyList<FilteredNavigationCard>>>(repository, "CreateMany", (object)new SavedGraphFilterRequest[] { new(11, "visible", 1), new(12, "hidden", 1, Deleted: true) });
            await create.Should().ThrowAsync<RepositorySavedResultException>();
        }
        context.ChangeTracker.Clear();
        (await Call<IReadOnlyList<ModelFilterRowSeed>>(fake, "All")).Should().HaveCount(7);
        (await context.Set<ModelFilterRow>().IgnoreQueryFilters().CountAsync()).Should().Be(7);
        await databaseTransaction.RollbackAsync(); await fixtureTransaction.RollbackAsync(); context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<ModelFilterRowSeed>>(fake, "All")).Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<ModelFilterRowSeed>>(real, "All")));
    }
}
