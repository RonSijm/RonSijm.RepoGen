using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedSetterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedSetterExecutionTests
{
    [Theory]
    [InlineData("Apply")]
    [InlineData("Conditional")]
    [InlineData("CurrentValues")]
    [InlineData("Execute")]
    public async Task ActualSqliteAndFixtureCommandsReturnIdenticalMappedState(string operation)
    {
        await using var context = Context();
        await SeedAsync(context);
        var real = Operations(context);
        var (memory, _, store) = Fixture();
        var request = new MappedSetterRequest(Extra: null);
        if (operation == "Execute")
            (await Call<int>(memory, operation, request)).Should().Be(await Call<int>(real, operation, request));
        else
        {
            object input = operation == "CurrentValues" ? 1 : request;
            (await Call<CommandResult<MappedSetterSeed>>(memory, operation, input)).Should()
                .BeEquivalentTo(await Call<CommandResult<MappedSetterSeed>>(real, operation, input));
        }
        (await Call<IReadOnlyList<MappedSetterSeed>>(memory, "Cards")).Should().BeEquivalentTo(await Call<IReadOnlyList<MappedSetterSeed>>(real, "Cards"));
        store.Find(1)!.Caption.Should().NotContain("getter behavior:");
        store.Find(2)!.Should().Be(Seeds()[1]);
    }

    [Theory]
    [InlineData(false, 1, 2)]
    [InlineData(true, 1, 2)]
    [InlineData(false, 2, 4)]
    [InlineData(true, 2, 4)]
    [InlineData(false, 99, 3)]
    [InlineData(true, 99, 3)]
    public async Task StaleHiddenAndMissingRowsNeverChangeMappedState(bool immediate, int id, int version)
    {
        await using var context = Context();
        await SeedAsync(context);
        var real = Operations(context);
        var (memory, _, store) = Fixture();
        var request = new MappedSetterRequest(id, Version: version);
        if (immediate)
        {
            (await Call<int>(real, "Execute", request)).Should().Be(0);
            (await Call<int>(memory, "Execute", request)).Should().Be(0);
        }
        else
        {
            var actual = await Call<CommandResult<MappedSetterSeed>>(real, "Apply", request);
            (await Call<CommandResult<MappedSetterSeed>>(memory, "Apply", request)).Should().BeEquivalentTo(actual);
            actual.Status.Should().Be(id == 1 ? CommandStatus.Conflict : CommandStatus.NotFound);
        }
        store.Snapshot().Should().BeEquivalentTo(Seeds());
        (await context.Set<MappedSetterNote>().OrderBy(row => row.Id).Select(row => EF.Property<int>(row, "audit")).ToListAsync()).Should().Equal(10, 20);
    }

    [Fact]
    public async Task FalseConditionalMappedPatchReturnsNoChanges()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        var request = new MappedSetterRequest(ChangeCaption: false);
        var expected = await Call<CommandResult<MappedSetterSeed>>(Operations(context), "Conditional", request);
        var actual = await Call<CommandResult<MappedSetterSeed>>(memory, "Conditional", request);
        expected.Status.Should().Be(CommandStatus.NoChanges);
        actual.Should().BeEquivalentTo(expected);
        data.PendingCount.Should().Be(0);
        store.Find(1)!.Should().Be(Seeds()[0]);
    }

    [Fact]
    public async Task StageKeepsSavedRowsUntilOneSharedSaveAndPreservesNullValues()
    {
        await using var context = Context();
        await SeedAsync(context);
        var real = Operations(context);
        var (memory, data, store) = Fixture();
        var request = new MappedSetterRequest(Extra: null);
        (await Call<CommandResult>(real, "Stage", request)).Status.Should().Be(CommandStatus.Staged);
        (await Call<CommandResult>(memory, "Stage", request)).Status.Should().Be(CommandStatus.Staged);
        store.Find(1)!.Should().Be(Seeds()[0]);
        (await Call<IReadOnlyList<MappedSetterSeed>>(real, "Cards")).Single().Should().Be(Seeds()[0]);
        await context.SaveChangesAsync();
        await data.SaveChangesAsync();
        store.Find(1)!.Should().Be(new MappedSetterSeed(1, "old", "new", 11, 4, null, "one"));
        (await Call<IReadOnlyList<MappedSetterSeed>>(memory, "Cards")).Should().BeEquivalentTo(await Call<IReadOnlyList<MappedSetterSeed>>(real, "Cards"));
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ImmediateMappedUpdateDoesNotFlushPendingStateAndOriginalTokensStillConflict()
    {
        await using var context = Context();
        await SeedAsync(context);
        var real = Operations(context);
        var (memory, data, store) = Fixture();
        await Call<CommandResult>(real, "Stage", new MappedSetterRequest(Caption: "staged"));
        await Call<CommandResult>(memory, "Stage", new MappedSetterRequest(Caption: "staged"));
        var request = new MappedSetterRequest(Caption: "immediate");
        (await Call<int>(real, "Execute", request)).Should().Be(1);
        (await Call<int>(memory, "Execute", request)).Should().Be(1);
        store.Find(1)!.Caption.Should().Be("immediate");
        data.PendingCount.Should().Be(1);
        context.ChangeTracker.Entries<MappedSetterNote>().Single().Property(nameof(MappedSetterNote.Caption)).CurrentValue.Should().Be("staged");
        Func<Task> efSave = () => context.SaveChangesAsync();
        Func<Task> fixtureSave = () => data.SaveChangesAsync();
        await efSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await fixtureSave.Should().ThrowAsync<RepositoryWriteConflictException>();
        store.Find(1)!.Caption.Should().Be("immediate");
        data.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task FailedTrackedSaveRestoresMappedCurrentOriginalAndEarlierDirtyState()
    {
        await using var context = Context();
        await SeedAsync(context);
        var row = await context.Set<MappedSetterNote>().SingleAsync(row => row.Id == 1);
        row.Title = "pending";
        context.ChangeTracker.DetectChanges();
        var entry = context.Entry(row);
        var caption = entry.Property(nameof(MappedSetterNote.Caption)).CurrentValue;
        var revision = entry.Property(MappedSetterContext.Version).OriginalValue;
        Func<Task> failure = () => Call<CommandResult>(Operations(context), "Break", 1);
        await failure.Should().ThrowAsync<Exception>();
        entry.Property(nameof(MappedSetterNote.Caption)).CurrentValue.Should().Be(caption);
        entry.Property(MappedSetterContext.Version).OriginalValue.Should().Be(revision);
        entry.Property(nameof(MappedSetterNote.Title)).CurrentValue.Should().Be("pending");
        entry.Property(nameof(MappedSetterNote.Title)).OriginalValue.Should().Be("old");
        entry.Property(nameof(MappedSetterNote.Title)).IsModified.Should().BeTrue();
        entry.Property(nameof(MappedSetterNote.Caption)).IsModified.Should().BeFalse();
        (await context.Set<MappedSetterNote>().Where(row => row.Id == 1).Select(row => row.Title).SingleAsync()).Should().Be("old");
    }

    [Fact]
    public async Task FixtureCheckpointsAndBaselinesRestoreMappedWrites()
    {
        var (memory, data, store) = Fixture();
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Call<CommandResult<MappedSetterSeed>>(memory, "Apply", new MappedSetterRequest());
            store.Find(1)!.Version.Should().Be(4);
        }
        store.Find(1)!.Should().Be(Seeds()[0]);
        await Call<int>(memory, "Execute", new MappedSetterRequest());
        data.RestoreBaseline();
        store.Find(1)!.Should().Be(Seeds()[0]);
    }

    [Fact]
    public async Task ScriptCanOverrideUnsupportedMappedWritesEvenOnEmptyStores()
    {
        using var context = Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(state: false, strict: false).Build()));
        var (memory, data, store) = Fixture(assembly);
        store.Clear();
        Func<Task> unsupported = () => Call<int>(memory, "Execute", new MappedSetterRequest());
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(memory, "OnExecuteAsync", 42);
        (await Call<int>(memory, "Execute", new MappedSetterRequest())).Should().Be(42);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ScalarMappedCommandsDoNotRequireTheAutomaticReadBackend()
    {
        using var context = Context();
        var builder = Builder().Configure(options => options.UseInMemoryTestQueries(false));
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (memory, _, store) = Fixture(assembly);
        var result = await Call<CommandResult<MappedSetterSeed>>(memory, "Apply", new MappedSetterRequest());
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Version.Should().Be(4);
        store.Find(1)!.Caption.Should().Be("new");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeInvocationRetainsSavedAndPendingState(bool immediate)
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, data, store) = Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        foreach (var repository in new[] { Operations(context), memory })
        {
            var method = repository.GetType().GetMethod(immediate ? "ExecuteAsync" : "ApplyAsync")!;
            Func<Task> cancel = () => (Task)method.Invoke(repository, [new MappedSetterRequest(), cancellation.Token])!;
            await cancel.Should().ThrowAsync<OperationCanceledException>();
        }
        data.PendingCount.Should().Be(0);
        store.Snapshot().Should().BeEquivalentTo(Seeds());
        (await context.Set<MappedSetterNote>().Where(row => row.Id == 1).Select(row => EF.Property<int>(row, "audit")).SingleAsync()).Should().Be(10);
    }

    private static Task<T> Call<T>(object repository, string method, params object?[] inputs)
        => MappedStateTestHarness.Call<T>(repository, method + "Async", inputs);
}
