using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryWriteExecutionTests
{
    [Fact]
    public async Task GeneratedStageSharedSaveAndNamedReadsUseOneCommittedFixture()
    {
        var (note, audit, data) = Repositories();
        (await Invoke<CommandResult>(note, "StageRenameAsync", new MemoryWriteRequest(1, "renamed"))).Status.Should().Be(CommandStatus.Staged);
        (await Invoke<CommandResult>(audit, "StageAuditAsync", new MemoryWriteRequest(3, "audit"))).Status.Should().Be(CommandStatus.Staged);
        (await Invoke<MemoryWriteView?>(note, "FindAsync", 1))!.Title.Should().Be("old");
        (await data.SaveChangesAsync()).Should().Be(2);
        (await Invoke<MemoryWriteView?>(note, "FindAsync", 1)).Should().Be(new MemoryWriteView(1, "renamed", 2));
        AuditStore(data).Find(3)!.Text.Should().Be("audit");
    }
    [Fact]
    public async Task SavedResultsMissingVisibilityAndStaleTokensHaveExplicitOutcomes()
    {
        var (note, _, data) = Repositories();
        var created = await Invoke<CommandResult<MemoryWriteView>>(note, "CreateAsync", new MemoryWriteRequest(3, "new"));
        created.Value.Should().Be(new MemoryWriteView(3, "new", 1));
        var renamed = await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(3, "next"));
        renamed.Value.Should().Be(new MemoryWriteView(3, "next", 2));
        (await Invoke<CommandResult<MemoryWriteView>>(note, "DeleteAsync", new MemoryWriteRequest(3, "", 1))).Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(2, "bad"))).Status.Should().Be(CommandStatus.NotFound);
        var deleted = await Invoke<CommandResult<MemoryWriteView>>(note, "DeleteAsync", new MemoryWriteRequest(3, "", 2));
        deleted.Value.Should().Be(new MemoryWriteView(3, "next", 2));
        NoteStore(data).Find(3).Should().BeNull();
        data.PendingCount.Should().Be(0);
    }
    [Fact]
    public async Task GuardAndConditionalAssignmentFailuresNeverStageWrites()
    {
        var (note, _, data) = Repositories();
        Func<Task> invalid = () => Invoke<CommandResult<MemoryWriteView>>(note, "CreateAsync", new MemoryWriteRequest(0, "bad"));
        await invalid.Should().ThrowAsync<ArgumentException>().WithMessage("*Positive id required*");
        (await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(1, "unsupplied", Supplied: false))).Status.Should().Be(CommandStatus.NoChanges);
        data.PendingCount.Should().Be(0);
        NoteStore(data).Find(1)!.Title.Should().Be("old");
    }
    [Fact]
    public async Task ExplicitTransactionsRollbackSavedAndStagedChangesWithoutChangingBaselines()
    {
        var (note, audit, data) = Repositories();
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(1, "saved"));
            await Invoke<CommandResult>(audit, "StageAuditAsync", new MemoryWriteRequest(3, "pending"));
        }
        NoteStore(data).Find(1)!.Version.Should().Be(1);
        AuditStore(data).Count.Should().Be(0);
        data.PendingCount.Should().Be(0);
        data.RestoreBaseline();
    }
    [Fact]
    public async Task ScriptOverridesAndResetDoNotAccidentallyExecuteTheWriteBackend()
    {
        var (note, _, data) = Repositories();
        ScriptedTestDoubleTestHarness.Returns(note, "OnRenameAsync", new CommandResult<MemoryWriteView>(CommandStatus.NotFound));
        (await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(1, "scripted"))).Status.Should().Be(CommandStatus.NotFound);
        NoteStore(data).Find(1)!.Title.Should().Be("old");
        note.GetType().GetMethod("ResetTestDouble")!.Invoke(note, []);
        (await Invoke<CommandResult<MemoryWriteView>>(note, "RenameAsync", new MemoryWriteRequest(1, "backend"))).Status.Should().Be(CommandStatus.Applied);
        NoteStore(data).Find(1)!.Title.Should().Be("backend");
    }
}
