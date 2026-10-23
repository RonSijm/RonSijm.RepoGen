using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualGeneratedSqliteReconciliationMatchesBothFilteredFixtureShapes(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new FilteredCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Parents()); context.AddRange(Children()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionParentRepositoryOperations")!, context)!;
        var (parent, _, data) = Fixture(mapped);
        async Task Equal<T>(string name, object input)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(parent, name, input)).Should()
                .Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, input)), name);
        await Equal<CommandResult<MemoryCollectionView>>("SaveItems", new MemoryCollectionRequest(1, 10, "saved", [new("a", "updated")]));
        var sqlRows = await context.Set<MemoryCollectionChild>().IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new FilteredCollectionChildSeed(row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden)).ToArrayAsync();
        Rows(data, mapped).Should().Equal(sqlRows); context.ChangeTracker.Clear();
        await Equal<CommandResult<FilteredCollectionView>>("SavedItems", new MemoryCollectionRequest(1, 10, "hidden update", [new("a", "deleted", 2)], 2));
        context.ChangeTracker.Clear();
        await Equal<CommandResult<MemoryCollectionView>>("Bypass", new MemoryCollectionRequest(1, 10, "bypass", [], 3));
        context.ChangeTracker.Clear();
        await Equal<CommandResult<MemoryCollectionView>>("SaveItems", new MemoryCollectionRequest(3, -1, "blocked", []));
        (await context.Set<MemoryCollectionChild>().IgnoreQueryFilters().CountAsync()).Should().Be(5);
        Rows(data, mapped).Should().HaveCount(5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HiddenLogicalKeyCollisionsRemainAnExplicitProviderConstraintBoundary(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new FilteredCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Parents()); context.AddRange(Children()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryCollectionParentRepositoryOperations")!, context)!;
        var (parent, _, data) = Fixture(mapped);
        var input = new MemoryCollectionRequest(1, 10, "new logical key", [new("deleted", "new value")]);
        // A filtered match is an addition, not permission to fetch/move/update the hidden row.
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", input)).Succeeded.Should().BeTrue();
        Rows(data, mapped).Count(row => row.ParentId == 1 && row.Key == "deleted").Should().Be(2);
        Func<Task> sql = async () => await Invoke<Task<CommandResult<MemoryCollectionView>>>(real, "SaveItems", input);
        await sql.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Clear();
        (await context.Set<MemoryCollectionChild>().IgnoreQueryFilters().CountAsync()).Should().Be(6);
    }
}
