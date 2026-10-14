using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NullTargetReadContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedNullSafeResultsAgreeWithSqliteForBothDirectionsAndStoreShapes(bool mapped, bool both)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = OptionalJoinTestHarness.Context(both, new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Tags()); context.AddRange(OptionalJoinTestHarness.Links(both));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, NullTargetReadTestHarness.Builder(mapped, both).Build());
        const string ns = "Generated.ManyRead.Persistence.";
        var realOwner = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryOperations")!, context)!;
        var realTag = Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryOperations")!, context)!;
        var (fakeOwner, fakeTag, _) = NullTargetReadTestHarness.Repositories(mapped, both);
        async Task Equal<T>(object real, object fake, string operation, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, operation, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, operation, inputs)), operation);
        await Equal<IReadOnlyList<NullTargetReadCard>>(realOwner, fakeOwner, "CardsAsync");
        await Equal<IReadOnlyList<long>>(realOwner, fakeOwner, "CountsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, fakeOwner, "NamedCountsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, fakeOwner, "SourceAsync");
        await Equal<IReadOnlyList<int>>(realOwner, fakeOwner, "HasMissingAsync");
        await Equal<IReadOnlyList<int>>(realOwner, fakeOwner, "SearchAsync", "Blue");
        await Equal<IReadOnlyList<List<string>>>(realOwner, fakeOwner, "FilteredAsync");
        if (both) await Equal<IReadOnlyList<int>>(realTag, fakeTag, "OwnersAsync");
        else await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, fakeTag, "OwnersAsync");
        await Equal<PagedResult<NullTargetReadCard>>(realOwner, fakeOwner, "PageAsync", 2, 1); context.ChangeTracker.Entries().Should().BeEmpty();
        await Equal<CommandResult<NullTargetReadCard>>(realOwner, fakeOwner, "RenameAsync", 1);
        IReadOnlyList<ManyReadPatch> input = [new(2, "second"), new(1, "first")];
        await Equal<CommandResult<IReadOnlyList<NullTargetReadCard>>>(realOwner, fakeOwner, "RenameManyAsync", input);
        context.Set<ManyReadLink>().Count().Should().Be(both ? 7 : 6);
    }
}
