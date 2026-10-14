using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchExecutionTests
{
    private static Task<TResult> Batch<TResult>(object repository, string name, IReadOnlyList<MemoryWriteRequest> items)
        => Invoke<TResult>(repository, name, (object)items);
    [Fact]
    public async Task BatchesStageAndSaveApprovedRowsWithOrderedResultsAndSharedCounts()
    {
        var (note, audit, data) = MemoryBatchTestHarness.Repositories();
        var inputs = new MemoryWriteRequest[] { new(2, "second"), new(1, "first") };
        (await Batch<CommandResult>(note, "StageManyAsync", inputs)).Status.Should().Be(CommandStatus.Staged);
        (await Invoke<IReadOnlyList<MemoryWriteView>>(note, "ListAsync")).Select(row => row.Title).Should().Equal("one", "two");
        (await data.SaveChangesAsync()).Should().Be(2);
        await Invoke<CommandResult>(audit, "StageAuditAsync", new MemoryWriteRequest(10, "audit"));
        var saved = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", new MemoryWriteRequest[] { new(2, "B", 2), new(1, "A", 2) });
        saved.Status.Should().Be(CommandStatus.Applied); saved.AffectedRows.Should().Be(3);
        saved.Value.Should().Equal(new MemoryWriteView(2, "B", 3), new MemoryWriteView(1, "A", 3));
        AuditStore(data).Find(10)!.Text.Should().Be("audit");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden")]
    [InlineData("stale")]
    public async Task LastTargetFailureCannotPartiallyApplyAnEarlierTarget(string kind)
    {
        var (note, _, data) = MemoryBatchTestHarness.Repositories();
        var second = kind == "missing" ? new MemoryWriteRequest(99, "missing") : kind == "hidden" ? new(3, "hidden") : new(2, "stale", 0);
        var result = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", new MemoryWriteRequest[] { new(1, "must not apply"), second });
        result.Status.Should().Be(kind == "stale" ? CommandStatus.Conflict : CommandStatus.NotFound);
        data.PendingCount.Should().Be(0); NoteStore(data).Find(1)!.Title.Should().Be("one");
    }

    [Fact]
    public async Task EmptyUnsuppliedUnchangedAndPartiallyAssignedBatchesReuseProductionOutcomes()
    {
        var (note, _, data) = MemoryBatchTestHarness.Repositories();
        (await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", Array.Empty<MemoryWriteRequest>())).Status.Should().Be(CommandStatus.NoChanges);
        var none = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "UnversionedAsync", new MemoryWriteRequest[] { new(1, "ignored", Supplied: false) });
        none.Status.Should().Be(CommandStatus.NoChanges);
        var unchanged = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "UnversionedAsync", new MemoryWriteRequest[] { new(1, "one") });
        unchanged.Status.Should().Be(CommandStatus.Applied); unchanged.AffectedRows.Should().Be(0);
        var mixed = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "UnversionedAsync", new MemoryWriteRequest[] { new(2, "ignored", Supplied: false), new(1, "new") });
        mixed.AffectedRows.Should().Be(1); mixed.Value!.Select(row => row.Title).Should().Equal("two", "new");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteManyReturnsPreDeletionValuesAndCheckpointRollbackRestoresAllRows()
    {
        var (note, _, data) = MemoryBatchTestHarness.Repositories();
        await using (await data.BeginTransactionAsync())
        {
            var result = await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "DeleteManyAsync", new MemoryWriteRequest[] { new(2, ""), new(1, "") });
            result.Value!.Select(row => row.Title).Should().Equal("two", "one"); NoteStore(data).Count.Should().Be(1);
        }
        NoteStore(data).Count.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateAndOversizedInputsFailBeforeStaging(bool oversized)
    {
        var (note, _, data) = MemoryBatchTestHarness.Repositories();
        var inputs = oversized ? new MemoryWriteRequest[] { new(1, ""), new(2, ""), new(3, ""), new(4, "") } : [new(1, ""), new(1, "")];
        Func<Task> invoke = () => Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", inputs);
        await invoke.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ScriptsOverrideAutomaticBatchesAndResetLeavesFixtureStateAlone()
    {
        var (note, _, data) = MemoryBatchTestHarness.Repositories();
        ScriptedTestDoubleTestHarness.Returns(note, "OnRenameManyAsync", new CommandResult<IReadOnlyList<MemoryWriteView>>(CommandStatus.NotFound));
        var items = new MemoryWriteRequest[] { new(1, "backend"), new(2, "second") };
        (await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", items)).Status.Should().Be(CommandStatus.NotFound);
        NoteStore(data).Find(1)!.Title.Should().Be("one"); data.PendingCount.Should().Be(0);
        note.GetType().GetMethod("ResetTestDouble")!.Invoke(note, []);
        (await Batch<CommandResult<IReadOnlyList<MemoryWriteView>>>(note, "RenameManyAsync", items)).Status.Should().Be(CommandStatus.Applied);
        NoteStore(data).Find(1)!.Title.Should().Be("backend");
    }
}
