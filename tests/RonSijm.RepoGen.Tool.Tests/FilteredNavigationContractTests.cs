using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredNavigationTestHarness;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedSqliteAndFixtureMatchFilteredCollectionsAndOptionalReferences(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelFilterContext(new DbContextOptionsBuilder<ModelFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ModelFilterTestHarness.Folders()); context.AddRange(ModelFilterTestHarness.Seeds());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, FilteredNavigationTestHarness.Builder(mapped).Build());
        var realRows = Activator.CreateInstance(assembly.GetType(ModelFilterTestHarness.Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, context)!;
        var realFolders = Activator.CreateInstance(assembly.GetType(ModelFilterTestHarness.Namespace + ".Persistence.ModelFilterFolderRepositoryOperations")!, context)!;
        var (fakeRows, fakeFolders, _) = Fixture(mapped);
        async Task Equal<T>(object fake, object real, string method, params object?[] input)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        foreach (var name in new[] { "Cards", "All", "From" }) await Equal<IReadOnlyList<FilteredNavigationCard>>(fakeRows, realRows, name + "Async");
        foreach (var name in new[] { "Cards", "All", "Loaded" }) await Equal<IReadOnlyList<FilteredNavigationFolderCard>>(fakeFolders, realFolders, name + "Async");
        await Equal<IReadOnlyList<int>>(fakeRows, realRows, "VisibleFolderAsync"); await Equal<IReadOnlyList<int>>(fakeRows, realRows, "JoinedAsync");
        await Equal<IReadOnlyList<int>>(fakeFolders, realFolders, "AnyAsync", "deleted"); await Equal<IReadOnlyList<int>>(fakeFolders, realFolders, "AnyAsync", "hidden");
        await Equal<PagedResult<FilteredNavigationCard>>(fakeRows, realRows, "PageAsync", 2, 1);
        await Equal<PagedResult<FilteredNavigationFolderCard>>(fakeFolders, realFolders, "PageAsync", 1, 1);
        var actual = new List<FilteredNavigationFolderCard>(); await foreach (var row in Invoke<IAsyncEnumerable<FilteredNavigationFolderCard>>(realFolders, "StreamAsync")) actual.Add(row);
        var simulated = new List<FilteredNavigationFolderCard>(); await foreach (var row in Invoke<IAsyncEnumerable<FilteredNavigationFolderCard>>(fakeFolders, "StreamAsync")) simulated.Add(row);
        JsonSerializer.Serialize(simulated).Should().Be(JsonSerializer.Serialize(actual));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
