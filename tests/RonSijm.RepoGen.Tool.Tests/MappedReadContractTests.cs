using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MappedReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadContractTests
{
    [Fact]
    public async Task GeneratedSqliteAndFixtureReadsUseTheSameMappedColumnsWithoutGetters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MappedReadContext(new DbContextOptionsBuilder<MappedReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Folders())
        {
            var entry = context.Add(new MappedReadFolder { Id = seed.Id, Name = seed.Name });
            entry.Property("hidden").CurrentValue = seed.Hidden;
        }
        foreach (var seed in MappedReadTestHarness.Seeds())
        {
            var entry = context.Add(new MappedReadItem { Id = seed.Id, Name = seed.Name, PublicRank = seed.Rank, FolderId = seed.FolderId });
            entry.Property(nameof(MappedReadItem.Caption)).CurrentValue = seed.Caption;
            entry.Property("audit").CurrentValue = seed.Audit;
            entry.Property("archived").CurrentValue = seed.Archived;
            entry.Property("state").CurrentValue = seed.State;
            entry.Property("fingerprint").CurrentValue = seed.Fingerprint;
            entry.Property(MappedReadContext.Tag).CurrentValue = seed.Tag;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        MappedReadItem.GetterCalls.Value = 0;
        var assembly = SavedResultTestHarness.CompileProduction(context, MappedReadTestHarness.Builder().Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedReadItemRepositoryOperations")!, context)!;
        var realFolders = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedReadFolderRepositoryOperations")!, context)!;
        var (fake, fakeFolders, _) = Fixture();
        async Task Equal<T>(string method, params object?[] input) =>
            JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        foreach (var name in new[] { "Cards", "Source", "All", "IncludingHidden" })
        {
            await Equal<IReadOnlyList<MappedReadCard>>(name + "Async");
        }
        await Equal<MappedReadCard?>("GetAsync", 1);
        await Equal<MappedReadCard?>("GetAsync", 3);
        await Equal<int>("CountAsync");
        await Equal<IReadOnlyList<int>>("ByRankAsync", 20);
        await Equal<IReadOnlyList<string>>("NamesAsync");
        await Equal<IReadOnlyList<string>>("TagsAsync");
        await Equal<IReadOnlyList<string>>("FolderNamesAsync");
        await Equal<IReadOnlyList<MappedReadTotal>>("TotalsAsync");
        await Equal<IReadOnlyList<MappedReadCard>>("SortedAsync", "Name", false);
        await Equal<IReadOnlyList<MappedReadCard>>("SortedAsync", "Caption", true);
        await Equal<PagedResult<MappedReadCard>>("PageAsync", 2, 2);
        JsonSerializer.Serialize(await Invoke<Task<IReadOnlyList<List<string>>>>(fakeFolders, "CaptionsAsync")).Should()
            .Be(JsonSerializer.Serialize(await Invoke<Task<IReadOnlyList<List<string>>>>(realFolders, "CaptionsAsync")));
        var expected = new List<MappedReadCard>();
        await foreach (var row in Invoke<IAsyncEnumerable<MappedReadCard>>(real, "StreamAsync"))
        {
            expected.Add(row);
        }
        var actual = new List<MappedReadCard>();
        await foreach (var row in Invoke<IAsyncEnumerable<MappedReadCard>>(fake, "StreamAsync"))
        {
            actual.Add(row);
        }
        JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        MappedReadItem.GetterCalls.Value.Should().Be(0);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
