using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedBatchTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedBatchExecutionTests
{
    [Fact]
    public async Task ConstantsNullableValuesAndMixedSettersRespectDeclarationOrder()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, _, _) = Fixture();
        var actual = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "ConstantBatch", new MappedSetterRequest());
        (await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "ConstantBatch", new MappedSetterRequest())).Should().BeEquivalentTo(actual);
        actual.Status.Should().Be(CommandStatus.Applied);
        actual.Value!.Single().Should().Be(MappedSetterTestHarness.Seeds()[0] with { Title = "mixed", Caption = "caption40", Audit = 40, Extra = null });
    }

    [Theory]
    [InlineData("SaveBatch")]
    [InlineData("CurrentBatch")]
    public async Task RealSqliteAndFixtureReturnInputOrderedMappedValues(string operation)
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, _, store) = Fixture();
        MappedSetterRequest[] items = [new(3, "third", Version: 5, Extra: null), new(1, "first", Extra: "value")];
        var actual = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), operation, items);
        var simulated = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, operation, items);
        actual.Status.Should().Be(CommandStatus.Applied);
        actual.AffectedRows.Should().Be(2);
        actual.Value!.Select(row => row.Id).Should().Equal(3, 1);
        actual.Value!.Select(row => row.Audit).Should().Equal(31, 11);
        actual.Value!.Select(row => row.Version).Should().Equal(6, 4);
        actual.Value!.Select(row => row.Caption).Should().Equal(operation == "CurrentBatch" ? ["second!", "caption!"] : ["third", "first"]);
        simulated.Should().BeEquivalentTo(actual);
        store.Find(2)!.Should().Be(MappedSetterTestHarness.Seeds()[1]);
        (await Cards(memory)).Should().BeEquivalentTo(await Cards(Operations(context)));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("hidden")]
    [InlineData("missing")]
    public async Task FailedLaterItemRestoresAllMappedState(string scenario)
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        // SQL visibility must exclude this row even if it is already tracked.
        await context.Set<MappedSetterNote>().SingleAsync(row => row.Id == 2);
        var (memory, data, store) = Fixture();
        var second = scenario switch
        {
            "stale" => new MappedSetterRequest(3, Version: 0),
            "hidden" => new MappedSetterRequest(2, Version: 4),
            _ => new MappedSetterRequest(99)
        };
        var actual = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "SaveBatch", new(1, "must restore"), second);
        (await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "SaveBatch", new(1, "must restore"), second)).Should().BeEquivalentTo(actual);
        actual.Status.Should().Be(scenario == "stale" ? CommandStatus.Conflict : CommandStatus.NotFound);
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        data.PendingCount.Should().Be(0);
        store.Snapshot().Should().BeEquivalentTo(MappedSetterTestHarness.Seeds().Append(Third));
        (await Cards(Operations(context))).Single(row => row.Id == 1).Should().Be(MappedSetterTestHarness.Seeds()[0]);
    }

    [Fact]
    public async Task StageKeepsSavedStateUntilOneSharedSave()
    {
        var saves = new SaveProbe();
        await using var context = new MappedSetterContext(new DbContextOptionsBuilder<MappedSetterContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(saves).Options);
        await SeedAsync(context);
        saves.Calls = 0;
        var (memory, data, store) = Fixture();
        var real = Operations(context);
        MappedSetterRequest[] items = [new(3, "third", Version: 5, Extra: null), new(1, "first")];
        (await Call<CommandResult>(real, "StageBatch", items)).Status.Should().Be(CommandStatus.Staged);
        (await Call<CommandResult>(memory, "StageBatch", items)).Status.Should().Be(CommandStatus.Staged);
        saves.Calls.Should().Be(0);
        store.Find(3)!.Should().Be(Third);
        (await Cards(real)).Single(row => row.Id == 3).Should().Be(Third);
        (await context.SaveChangesAsync()).Should().Be(2);
        (await data.SaveChangesAsync()).Should().Be(2);
        saves.Calls.Should().Be(1);
        store.Find(3)!.Extra.Should().BeNull();
        (await Cards(memory)).Should().BeEquivalentTo(await Cards(real));
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task SavingBatchUsesOneSharedSaveAndExplicitFlagsWhenAutoDetectIsDisabled()
    {
        var saves = new SaveProbe();
        await using var context = new MappedSetterContext(new DbContextOptionsBuilder<MappedSetterContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(saves).Options);
        await SeedAsync(context);
        saves.Calls = 0;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var result = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "SaveBatch", new(1), new(3, Version: 5));
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Select(row => row.Audit).Should().Equal(11, 31);
        saves.Calls.Should().Be(1);
        context.ChangeTracker.AutoDetectChangesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ConditionalOmissionDoesNotMutateOrSave()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        MappedSetterRequest[] items = [new(1, ChangeCaption: false), new(3, Version: 5, ChangeCaption: false)];
        var actual = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "ConditionalBatch", items);
        actual.Status.Should().Be(CommandStatus.NoChanges);
        (await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "ConditionalBatch", items)).Should().BeEquivalentTo(actual);
        data.PendingCount.Should().Be(0);
        store.Snapshot().Should().BeEquivalentTo(MappedSetterTestHarness.Seeds().Append(Third));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("tooMany")]
    [InlineData("empty")]
    public async Task ExistingInputGuardsRunBeforeAnyMappedMutation(string scenario)
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        MappedSetterRequest[] items = scenario switch
        {
            "duplicate" => [new(1), new(1)],
            "tooMany" => [new(1), new(3), new(4), new(5)],
            _ => []
        };
        foreach (var repository in new[] { Operations(context), memory })
        {
            if (scenario == "empty") (await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(repository, "SaveBatch", items)).Status.Should().Be(CommandStatus.NoChanges);
            else
            {
                Func<Task> invalid = () => Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(repository, "SaveBatch", items);
                await invalid.Should().ThrowAsync<ArgumentException>();
            }
        }
        context.ChangeTracker.Entries().Should().BeEmpty();
        data.PendingCount.Should().Be(0);
        store.Snapshot().Should().BeEquivalentTo(MappedSetterTestHarness.Seeds().Append(Third));
    }

    [Fact]
    public async Task PendingMappedTargetChangesAreRejectedBeforeBatchMutation()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var row = await context.Set<MappedSetterNote>().SingleAsync(row => row.Id == 3);
        context.Entry(row).Property("audit").CurrentValue = 90;
        Func<Task> invalid = () => Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "SaveBatch", new(1), new(3, Version: 5));
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*");
        context.Entry(row).Property("audit").CurrentValue.Should().Be(90);
        (await Cards(Operations(context))).Single(value => value.Id == 1).Audit.Should().Be(10);
    }

    [Fact]
    public async Task ProviderFailureRestoresEveryMappedValueAndPreservesUnrelatedPendingState()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var hidden = await context.Set<MappedSetterNote>().SingleAsync(row => row.Id == 2);
        hidden.Title = "unrelated pending";
        Func<Task> failure = () => Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(Operations(context), "FailBatch",
            new(1, "first changes"), new(3, "fail", Version: 5));
        await failure.Should().ThrowAsync<DbUpdateException>();
        var targets = context.ChangeTracker.Entries<MappedSetterNote>().Where(entry => entry.Entity.Id != 2).ToArray();
        targets.Should().HaveCount(2).And.OnlyContain(entry => entry.State == EntityState.Unchanged);
        targets.Single(entry => entry.Entity.Id == 1).Property("audit").CurrentValue.Should().Be(10);
        targets.Single(entry => entry.Entity.Id == 3).Property(MappedSetterContext.Version).OriginalValue.Should().Be(5);
        hidden.Title.Should().Be("unrelated pending");
        context.Entry(hidden).State.Should().Be(EntityState.Modified);
        (await Cards(Operations(context))).Should().BeEquivalentTo(new[] { MappedSetterTestHarness.Seeds()[0], Third });
    }

    [Fact]
    public async Task LaterValueExpressionFailureRestoresEarlierRowsBeforeAnySave()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        foreach (var repository in new[] { Operations(context), memory })
        {
            Func<Task> failure = () => Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(repository, "FailValueBatch", new(1), new(3, Version: 5));
            await failure.Should().ThrowAsync<DivideByZeroException>();
        }
        data.PendingCount.Should().Be(0);
        store.Snapshot().Should().BeEquivalentTo(MappedSetterTestHarness.Seeds().Append(Third));
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await Cards(Operations(context))).Should().BeEquivalentTo(new[] { MappedSetterTestHarness.Seeds()[0], Third });
    }

    [Fact]
    public async Task CancellationLeavesSavedAndPendingMappedStateUntouched()
    {
        await using var context = MappedSetterTestHarness.Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        foreach (var repository in new[] { Operations(context), memory })
        {
            Func<Task> cancel = () => (Task)repository.GetType().GetMethod("SaveBatchAsync")!
                .Invoke(repository, [(IReadOnlyList<MappedSetterRequest>)new[] { new MappedSetterRequest() }, new CancellationToken(true)])!;
            await cancel.Should().ThrowAsync<OperationCanceledException>();
        }
        data.PendingCount.Should().Be(0);
        context.ChangeTracker.Entries().Should().BeEmpty();
        store.Snapshot().Should().BeEquivalentTo(MappedSetterTestHarness.Seeds().Append(Third));
    }

    [Fact]
    public async Task FixtureCheckpointsAndBaselinesRestoreNamedBatchWrites()
    {
        var (memory, data, store) = Fixture();
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "SaveBatch", new(1), new(3, Version: 5));
            store.Find(3)!.Version.Should().Be(6);
        }
        store.Find(3)!.Should().Be(Third);
        await Call<CommandResult>(memory, "StageBatch", new MappedSetterRequest(1));
        Action resetPending = () => data.RestoreBaseline();
        resetPending.Should().Throw<InvalidOperationException>().WithMessage("*pending writes*");
        await data.SaveChangesAsync();
        data.RestoreBaseline();
        data.PendingCount.Should().Be(0);
        store.Find(1)!.Should().Be(MappedSetterTestHarness.Seeds()[0]);
    }

    [Fact]
    public async Task UnsupportedMappedBatchesRequireScriptsEvenForEmptyInputsAndStores()
    {
        using var context = MappedSetterTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(state: false, strict: false).Build());
        var (memory, data, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        store.Clear();
        Func<Task> unsupported = () => Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "SaveBatch");
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        var scripted = new CommandResult<IReadOnlyList<MappedSetterSeed>>(CommandStatus.Applied, 42);
        ScriptedTestDoubleTestHarness.Returns(memory, "OnSaveBatchAsync", scripted);
        (await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "SaveBatch")).Should().Be(scripted);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task MappedBatchSavedScalarsDoNotRequireTheAutomaticReadBackend()
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder().Configure(options => options.UseInMemoryTestQueries(false));
        var (memory, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        var result = await Call<CommandResult<IReadOnlyList<MappedSetterSeed>>>(memory, "SaveBatch", new(3, Version: 5), new(1));
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Select(row => row.Id).Should().Equal(3, 1);
        store.Find(1)!.Audit.Should().Be(11);
    }

    private static Task<IReadOnlyList<MappedSetterSeed>> Cards(object repository)
        => MappedStateTestHarness.Call<IReadOnlyList<MappedSetterSeed>>(repository, "CardsAsync");

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        internal int Calls { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }
}
