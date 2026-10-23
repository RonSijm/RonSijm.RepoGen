using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ShadowReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadContractTests
{
    [Fact]
    public async Task GeneratedReadsMatchActualSqliteShadowColumnsFiltersAndConvertedValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ShadowReadContext(new DbContextOptionsBuilder<ShadowReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Folders())
        {
            var entry = context.Add(new ShadowReadFolder { Id = seed.Id, Name = seed.Name });
            entry.Property("Archived").CurrentValue = seed.Archived;
        }
        foreach (var seed in ShadowReadTestHarness.Seeds())
        {
            var entry = context.Add(new ShadowReadNote { Id = seed.Id, Title = seed.Title, Hidden = seed.Hidden, FolderId = seed.FolderId });
            entry.Property("Rank").CurrentValue = seed.Rank;
            entry.Property("Code").CurrentValue = seed.Code;
            entry.Property("Score").CurrentValue = seed.Score;
            entry.Property("Deleted").CurrentValue = seed.Deleted;
            entry.Property("Status").CurrentValue = seed.Status;
            entry.Property("Fingerprint").CurrentValue = seed.Fingerprint;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, ShadowReadTestHarness.Builder().Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ShadowReadNoteRepositoryOperations")!, context)!;
        var realFolders = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ShadowReadFolderRepositoryOperations")!, context)!;
        var (fake, fakeFolders, _) = Fixture();
        async Task Equal<T>(string method, params object?[] input) =>
            JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        foreach (var name in new[] { "Cards", "All", "IncludingHidden", "Join" })
        {
            await Equal<IReadOnlyList<ShadowReadCard>>(name + "Async");
        }
        await Equal<ShadowReadCard?>("GetAsync", 1);
        await Equal<ShadowReadCard?>("GetAsync", 3);
        await Equal<ShadowReadCard?>("FirstAsync");
        await Equal<int>("CountAsync");
        await Equal<int>("MaximumAsync");
        await Equal<bool>("ExistsAsync", "a");
        await Equal<IReadOnlyList<int>>("RankedAsync", 20);
        await Equal<IReadOnlyList<ShadowReadGroup>>("GroupsAsync");
        await Equal<IReadOnlyDictionary<int, int>>("LookupAsync");
        await Equal<PagedResult<ShadowReadCard>>("PageAsync", 2, 2);
        await Equal<IReadOnlyList<string>>("FoldersAsync");
        await Equal<IReadOnlyList<bool?>>("ArchiveFlagsAsync");
        JsonSerializer.Serialize(await Invoke<Task<IReadOnlyList<List<int>>>>(fakeFolders, "RanksAsync")).Should()
            .Be(JsonSerializer.Serialize(await Invoke<Task<IReadOnlyList<List<int>>>>(realFolders, "RanksAsync")));
        var expected = new List<ShadowReadCard>();
        await foreach (var card in Invoke<IAsyncEnumerable<ShadowReadCard>>(real, "StreamAsync"))
        {
            expected.Add(card);
        }
        var actual = new List<ShadowReadCard>();
        await foreach (var card in Invoke<IAsyncEnumerable<ShadowReadCard>>(fake, "StreamAsync"))
        {
            actual.Add(card);
        }
        JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
