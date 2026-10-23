using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredManyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyContractTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ActualGeneratedOperationsMatchSqliteWithFilteredEndpointsAndExplicitJoinRows(bool mapped, bool optional)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options;
        await using ManyReadContext context = optional ? new FilteredManyOptionalContext(options) : new FilteredManyReadContext(options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(FilteredManyTestHarness.Owners());
        context.AddRange(FilteredManyTestHarness.Tags()); context.AddRange(FilteredManyTestHarness.Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, FilteredManyTestHarness.Builder(mapped, optional).Build());
        var ns = "Generated.ManyRead.Persistence.";
        var realOwner = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryOperations")!, context)!;
        var realTag = Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryOperations")!, context)!;
        var (owner, tag, _) = Fixture(mapped, optional);
        async Task Equal<T>(object real, object fake, string method, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, inputs)).Should()
                .Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, inputs)), method);
        await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, tag, "OwnersAsync");
        await Equal<IReadOnlyList<int>>(realOwner, owner, "RootBypassAsync");
        await Equal<IReadOnlyList<int>>(realOwner, owner, "SearchAsync", "Archived tag");
        await Equal<IReadOnlyList<List<string>>>(realOwner, owner, "AllAsync");
        IReadOnlyList<ManyReadPatch> batch = [new(2, "second"), new(1, "first")];
        if (optional)
        {
            await Equal<IReadOnlyList<NullTargetReadCard>>(realOwner, owner, "CardsAsync");
            await Equal<IReadOnlyList<long>>(realOwner, owner, "CountsAsync");
            await Equal<IReadOnlyList<int>>(realOwner, owner, "HasMissingAsync");
            await Equal<PagedResult<NullTargetReadCard>>(realOwner, owner, "PageAsync", 2, 1);
            await Equal<CommandResult<NullTargetReadCard>>(realOwner, owner, "RenameAsync", 2);
            await Equal<CommandResult<IReadOnlyList<NullTargetReadCard>>>(realOwner, owner, "RenameManyAsync", batch);
        }
        else
        {
            await Equal<IReadOnlyList<ManyReadCard>>(realOwner, owner, "CardsAsync");
            await Equal<IReadOnlyList<int>>(realOwner, owner, "SourceAsync");
            await Equal<IReadOnlyList<List<int>>>(realOwner, owner, "CyclesAsync");
            await Equal<PagedResult<List<string>>>(realOwner, owner, "PageAsync", 2, 1);
            await Equal<CommandResult<ManyReadCard>>(realOwner, owner, "RenameAsync", 1);
            await Equal<CommandResult<IReadOnlyList<ManyReadCard>>>(realOwner, owner, "RenameManyAsync", batch);
        }
        (await context.Set<ManyReadLink>().IgnoreQueryFilters().CountAsync()).Should().Be(6);
    }
}
