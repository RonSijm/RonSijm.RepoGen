using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyToManyContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSameGeneratedNamedOperationsMatchSqliteForBothFixtureShapes(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ManyReadContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Owners()); context.AddRange(Tags()); context.AddRange(Links()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        const string ns = "Generated.ManyRead.Persistence.";
        var realOwner = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryOperations")!, context)!;
        var realTag = Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryOperations")!, context)!;
        var (memoryOwner, memoryTag, _) = Repositories(mapped);
        async Task Equal<T>(object real, object fake, string name, params object?[] inputs)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, name, inputs)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, inputs)), name);
        await Equal<IReadOnlyList<ManyReadCard>>(realOwner, memoryOwner, "CardsAsync");
        await Equal<IReadOnlyList<List<string>>>(realOwner, memoryOwner, "VisibleTagsAsync");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "SearchAsync", "Blue");
        await Equal<IReadOnlyList<int>>(realOwner, memoryOwner, "SourceAsync");
        await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, memoryTag, "OwnersAsync");
        await Equal<IReadOnlyList<List<int>>>(realOwner, memoryOwner, "CyclesAsync");
        await Equal<PagedResult<List<string>>>(realOwner, memoryOwner, "PageAsync", 2, 1);
        context.ChangeTracker.Entries().Should().BeEmpty();
        await Equal<CommandResult<ManyReadCard>>(realOwner, memoryOwner, "RenameAsync", 1);
        IReadOnlyList<ManyReadPatch> batch = [new(2, "second"), new(1, "first")];
        await Equal<CommandResult<IReadOnlyList<ManyReadCard>>>(realOwner, memoryOwner, "RenameManyAsync", batch);
        context.Set<ManyReadLink>().Count().Should().Be(4); context.Set<ManyReadOwner>().Single(row => row.Id == 1).Name.Should().Be("first");
    }
}
