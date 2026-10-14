using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ModelDefaultTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelDefaultExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SentinelsGenerateButSuppliedZeroFalseNullAndEnumValuesSurvive(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        var unset = await Call<CommandResult<ModelDefaultData>>(repository, "Create", new ModelDefaultData(1));
        unset.Value.Should().Be(new ModelDefaultData(1, 5, true, ModelDefaultLevel.Standard, "model", 7, 10));
        var supplied = new ModelDefaultData(2, 0, false, ModelDefaultLevel.Basic, "", null, 0);
        (await Call<CommandResult<ModelDefaultData>>(repository, "Create", supplied)).Value.Should().Be(supplied);
        await Call<CommandResult>(repository, "Reset", 1);
        (await Call<IReadOnlyList<ModelDefaultData>>(repository, "Rows"))[0].Credits.Should().Be(-1, "insert defaults must not run on updates");
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StagingBatchesRollbackAndSeedsRemainIndependent(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        data.CaptureBaseline();
        (await Call<CommandResult>(repository, "StageCreate", new ModelDefaultData(1))).Status.Should().Be(CommandStatus.Staged);
        (await Call<IReadOnlyList<ModelDefaultData>>(repository, "Rows")).Should().BeEmpty();
        await data.SaveChangesAsync();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            var requests = new ModelDefaultData[] { new(3), new(2, 0, false, ModelDefaultLevel.Basic, "supplied", null, 0) };
            var result = await Call<CommandResult<IReadOnlyList<ModelDefaultData>>>(repository, "CreateMany", (object)requests);
            result.Value!.Select(row => row.Id).Should().Equal(3, 2);
            (await Call<IReadOnlyList<ModelDefaultData>>(repository, "Rows")).Should().HaveCount(3);
            await transaction.RollbackAsync();
        }
        data.PendingCount.Should().Be(0);
        (await Call<IReadOnlyList<ModelDefaultData>>(repository, "Rows")).Should().ContainSingle().Which.Credits.Should().Be(5);
        data.RestoreBaseline();
        if (mapped) ((InMemoryStore<ModelDefaultData, int>)data.GetType().GetProperty("ModelDefaultRowStore")!.GetValue(data)!).Seed(new(10));
        else ((InMemoryStore<ModelDefaultRow, int>)data.GetType().GetProperty("ModelDefaultRowStore")!.GetValue(data)!).Seed(new() { Id = 10 });
        (await Call<IReadOnlyList<ModelDefaultData>>(repository, "Rows")).Single().Credits.Should().Be(-1, "seeding is not insertion/default generation");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> cancel = async () => await (Task<CommandResult<ModelDefaultData>>)repository.GetType().GetMethod("CreateAsync")!.Invoke(repository, [new ModelDefaultData(11), cancellation.Token])!;
        await cancel.Should().ThrowAsync<OperationCanceledException>(); data.PendingCount.Should().Be(0);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitFactoriesWorkWithoutModelOptInAndWinWhenEnabled(bool enabled)
    {
        var (repository, _) = Fixture(true, Builder(enabled: enabled, manual: true));
        (await Call<CommandResult<ModelDefaultData>>(repository, "Create", new ModelDefaultData(1))).Value!.Credits.Should().Be(9);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedSqliteAndFixturesAgreeOnDefaultsSuppliedValuesBatchAndStage(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ModelDefaultContext(new DbContextOptionsBuilder<ModelDefaultContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ModelDefaultRowRepositoryOperations")!, context)!;
        var (memory, data) = Fixture(mapped);
        async Task Equal<T>(string name, params object?[] inputs) => JsonSerializer.Serialize(await Call<T>(memory, name, inputs))
            .Should().Be(JsonSerializer.Serialize(await Call<T>(real, name, inputs)), name);
        await Equal<CommandResult<ModelDefaultData>>("Create", new ModelDefaultData(1));
        await Equal<CommandResult<ModelDefaultData>>("Create", new ModelDefaultData(2, 0, false, ModelDefaultLevel.Basic, "", null, 0));
        await Equal<CommandResult<IReadOnlyList<ModelDefaultData>>>("CreateMany", (object)new ModelDefaultData[] { new(4), new(3, 15, false, ModelDefaultLevel.Basic, "provided", 0, 0) });
        await Equal<CommandResult>("StageCreate", new ModelDefaultData(5));
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); context.ChangeTracker.Clear();
        await Equal<CommandResult>("Reset", 1); await Equal<IReadOnlyList<ModelDefaultData>>("Rows");
    }
}
