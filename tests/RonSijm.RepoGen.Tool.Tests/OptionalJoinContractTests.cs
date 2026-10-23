using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalJoinContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedContractsAgreeWithSqliteForBothDirectionsAndStoreShapes(bool mapped, bool both)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = OptionalJoinTestHarness.Context(both, new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Owners()); context.AddRange(Tags()); context.AddRange(OptionalJoinTestHarness.Links(both));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, OptionalJoinTestHarness.Builder(mapped, both).Build());
        const string ns = "Generated.ManyRead.Persistence.";
        var realOwner = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryOperations")!, context)!;
        var realTag = Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryOperations")!, context)!;
        var (memoryOwner, memoryTag, _) = OptionalJoinTestHarness.Repositories(mapped, both);
        async Task Equal<T>(object real, object fake, string name, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, name, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, inputs)), name);
        await Equal<IReadOnlyList<ManyReadCard>>(realOwner, memoryOwner, "CardsAsync");
        await Equal<IReadOnlyList<List<string>>>(realOwner, memoryOwner, "VisibleTagsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "SearchAsync", "Blue");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "CountsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "NamedCountsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "SourceAsync");
        await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, memoryTag, "OwnersAsync");
        if (!both) await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, memoryTag, "LoadedOwnersAsync");
        await Equal<PagedResult<List<string>>>(realOwner, memoryOwner, "PageAsync", 2, 1);
        context.ChangeTracker.Entries().Should().BeEmpty();
        await Equal<CommandResult<ManyReadCard>>(realOwner, memoryOwner, "RenameAsync", 1);
        IReadOnlyList<ManyReadPatch> batch = [new(2, "second"), new(1, "first")];
        await Equal<CommandResult<IReadOnlyList<ManyReadCard>>>(realOwner, memoryOwner, "RenameManyAsync", batch);
        context.Set<ManyReadLink>().Count().Should().Be(both ? 7 : 6);
        (await Invoke<Task<IReadOnlyList<int>>>(realOwner, "RawCountsAsync")).Should().Equal(4, 1, 0);
    }
}
