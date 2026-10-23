using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ModelFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterContractTests
{
    [Fact]
    public async Task ExplicitFixtureHelperTranslationDoesNotAddSqlClientFilterFallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "helper");
        await context.Database.EnsureCreatedAsync(); context.AddRange(Folders()); context.AddRange(ModelFilterTestHarness.Seeds()); await context.SaveChangesAsync();
        var builder = Base(); builder.Entity<ModelFilterRow>().HasQuery("Read").OrderBy(row => row.Id).Select(row => row.Id).List();
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        var design = builder.Build(); var realAssembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(realAssembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, context)!;
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var fixture = Activator.CreateInstance(fakeAssembly.GetType(Namespace + ".Testing.ModelFilterGuardContextRepositoryFixture")!)!;
        var data = (RonSijm.RepoGen.Testing.InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        Store<ModelFilterRowSeed>(data, "ModelFilterRow").SeedRange(ModelFilterTestHarness.Seeds().Select(row => new ModelFilterRowSeed(row.Id, row.Title, row.Deleted, row.Hidden, row.FolderId)));
        var fake = Activator.CreateInstance(fakeAssembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!;
        (await Invoke<Task<IReadOnlyList<int>>>(fake, "ReadAsync")).Should().Equal(1, 4, 5);
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(real, "ReadAsync");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedFixtureAndSqliteApplyRootFiltersBeforeTheSameNamedReadPipelines(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelFilterContext(new DbContextOptionsBuilder<ModelFilterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Folders()); context.AddRange(ModelFilterTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, ModelFilterTestHarness.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelFilterRowRepositoryOperations")!, context)!;
        var (fake, _) = Fixture(mapped);
        async Task Equal<T>(string method, params object?[] input)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        await Equal<IReadOnlyList<ModelFilterCard>>("CardsAsync"); await Equal<ModelFilterCard?>("GetAsync", 2); await Equal<ModelFilterCard?>("GetAsync", 1);
        await Equal<int>("CountAsync"); await Equal<bool>("ExistsAsync", 2); await Equal<IReadOnlyDictionary<int, string>>("TitlesAsync");
        await Equal<IReadOnlyList<int>>("FoldersAsync"); await Equal<PagedResult<ModelFilterCard>>("PageAsync", 2, 1);
        foreach (var name in new[] { "IncludingDeleted", "IncludingHidden", "All", "Joined", "JoinedAll", "Correlated", "Self", "Union" })
            await Equal<IReadOnlyList<int>>(name + "Async");
        await Equal<PagedResult<int>>("JoinedPageAsync", 1, 1);
        var actual = new List<ModelFilterCard>(); await foreach (var row in Invoke<IAsyncEnumerable<ModelFilterCard>>(real, "StreamAsync")) actual.Add(row);
        var simulated = new List<ModelFilterCard>(); await foreach (var row in Invoke<IAsyncEnumerable<ModelFilterCard>>(fake, "StreamAsync")) simulated.Add(row);
        simulated.Should().Equal(actual); context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
