using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ContextFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundFixtureReadsMatchGeneratedSqliteRootsJoinsAndNavigationResults(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ContextFilterContext(new DbContextOptionsBuilder<ContextFilterContext>().UseSqlite(connection).Options, throwOnRead: false);
        await SeedSqlite(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped)); var provider = new ContextFilterLeaseProvider(context);
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, provider, new SqliteReturningWriter())!;
        var realFolder = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterFolderRepositoryOperations")!, provider)!;
        var key = new ContextFilterKey("A"); var (fake, fakeFolder, _, _) = Fixture(key, mapped);
        async Task Equal<T>(string name, params object?[] input)
            => JsonSerializer.Serialize(await Call<T>(fake, name, input)).Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, input)), name);
        foreach (var name in new[] { "Cards", "All", "Bypass" }) await Equal<IReadOnlyList<ModelFilterCard>>(name, new ContextFilterRequest(key));
        await Equal<int>("Count", new ContextFilterRequest(key));
        await Equal<IReadOnlyList<int>>("Joined", new ContextFilterRequest(key));
        await Equal<IReadOnlyList<ContextFilterCard>>("Related", new ContextFilterRequest(key));
        await Equal<PagedResult<ModelFilterCard>>("Page", new ContextFilterRequest(key), 2, 1);
        JsonSerializer.Serialize(await Call<IReadOnlyList<int>>(fakeFolder, "Collections", new ContextFilterRequest(key)))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<int>>(realFolder, "Collections", new ContextFilterRequest(key))));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundFixtureWritesMatchGeneratedSqliteSelectionFreshResultsAndNativeResults(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ContextFilterContext(new DbContextOptionsBuilder<ContextFilterContext>().UseSqlite(connection).Options, throwOnRead: false);
        await SeedSqlite(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder(mapped));
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, new ContextFilterLeaseProvider(context), new SqliteReturningWriter())!;
        var key = new ContextFilterKey("A"); var (fake, _, data, _) = Fixture(key, mapped);
        async Task Equal<T>(string name, params object?[] input)
            => JsonSerializer.Serialize(await Call<T>(fake, name, input)).Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, input)), name);
        await Equal<CommandResult<ModelFilterCard>>("Rename", new ContextFilterRequest(key, 2, "A"));
        await Equal<CommandResult<ModelFilterCard>>("Rename", new ContextFilterRequest(key, 1, "B")); context.ChangeTracker.Clear();
        foreach (var repository in new[] { fake, real })
        {
            Func<Task> saved = async () => await Call<CommandResult<ModelFilterCard>>(repository, "RenameSaved", new ContextFilterRequest(key, 5, "B"));
            await saved.Should().ThrowAsync<RepositorySavedResultException>();
        }
        context.ChangeTracker.Clear();
        await Equal<IReadOnlyList<ModelFilterCard>>("Cards", new ContextFilterRequest(key));
        await Equal<MutationResult<ModelFilterCard>>("Archive", new ContextFilterRequest(key));
        data.PendingCount.Should().Be(0); await Equal<int>("Count", new ContextFilterRequest(key));
        await Equal<int>("Consume", new ContextFilterRequest(key, 2));
        await Equal<IReadOnlyList<ModelFilterCard>>("All", new ContextFilterRequest(key));
    }

    private static async Task SeedSqlite(ContextFilterContext context)
    {
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new ModelFilterFolder { Id = 1 }, new ModelFilterFolder { Id = 2 }); context.AddRange(Seeds());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
