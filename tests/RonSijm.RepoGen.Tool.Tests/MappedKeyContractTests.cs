using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MappedKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyContractTests
{
    [Fact]
    public async Task GeneratedSqliteAndMappedFixturesMatchForBothDirectionsFiltersPagesAndFromRoots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MappedKeyContext(new DbContextOptionsBuilder<MappedKeyContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Owners())
        {
            var entry = context.Add(new MappedKeyOwner { Id = seed.Id, Name = seed.Name });
            entry.Property("Scope").CurrentValue = seed.Scope;
            entry.Property(nameof(MappedKeyOwner.Code)).CurrentValue = seed.Code;
            entry.Property("Archived").CurrentValue = seed.Archived;
        }
        foreach (var seed in Tags())
        {
            context.Add(new MappedKeyTag { Id = seed.Id, Name = seed.Name });
        }
        foreach (var seed in Children())
        {
            var entry = context.Add(new MappedKeyChild { Id = seed.Id, Name = seed.Name, Deleted = seed.Deleted });
            entry.Property("OwnerScope").CurrentValue = seed.OwnerScope;
            entry.Property("OwnerCode").CurrentValue = seed.OwnerCode;
        }
        foreach (var seed in Links())
        {
            var entry = context.Add(new MappedKeyLink { Id = seed.Id });
            entry.Property("OwnerScope").CurrentValue = seed.OwnerScope;
            entry.Property("ownerCode").CurrentValue = seed.OwnerCode;
            entry.Property("TagId").CurrentValue = seed.TagId;
            entry.Property("Removed").CurrentValue = seed.Removed;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        MappedKeyOwner.GetterCalls.Value = 0;
        var realAssembly = SavedResultTestHarness.CompileProduction(context, MappedKeyTestHarness.Builder().Build());
        object Real(string entity) => Activator.CreateInstance(realAssembly.GetType(Namespace + ".Persistence." + entity + "RepositoryOperations")!, context)!;
        var realOwner = Real(nameof(MappedKeyOwner));
        var realChild = Real(nameof(MappedKeyChild));
        var realTag = Real(nameof(MappedKeyTag));
        var (fakeOwner, fakeChild, fakeTag, _) = Fixture();
        async Task Equal<T>(object actual, object expected, string method, params object?[] input) =>
            JsonSerializer.Serialize(await Invoke<Task<T>>(actual, method, input)).Should()
                .Be(JsonSerializer.Serialize(await Invoke<Task<T>>(expected, method, input)), method);
        await Equal<IReadOnlyList<MappedKeyCard>>(fakeOwner, realOwner, "CardsAsync");
        await Equal<IReadOnlyList<MappedKeyCard>>(fakeOwner, realOwner, "AllAsync");
        await Equal<IReadOnlyList<MappedKeyChildCard>>(fakeChild, realChild, "CardsAsync");
        await Equal<IReadOnlyList<List<string>>>(fakeTag, realTag, "OwnersAsync");
        await Equal<IReadOnlyList<int>>(fakeOwner, realOwner, "SourceAsync");
        await Equal<IReadOnlyList<int>>(fakeOwner, realOwner, "HasTagAsync", "blue");
        await Equal<PagedResult<MappedKeyCard>>(fakeOwner, realOwner, "PageAsync", 2, 1);
        var expectedRows = new List<MappedKeyCard>();
        await foreach (var row in Invoke<IAsyncEnumerable<MappedKeyCard>>(realOwner, "StreamAsync"))
        {
            expectedRows.Add(row);
        }
        var actualRows = new List<MappedKeyCard>();
        await foreach (var row in Invoke<IAsyncEnumerable<MappedKeyCard>>(fakeOwner, "StreamAsync"))
        {
            actualRows.Add(row);
        }
        JsonSerializer.Serialize(actualRows).Should().Be(JsonSerializer.Serialize(expectedRows));
        MappedKeyOwner.GetterCalls.Value.Should().Be(0);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
