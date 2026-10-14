using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultExecutionTests
{
    [Fact]
    public async Task ReconciliationReturnsSavedAdditionsUpdatesAndDeletesNotASeededGraph()
    {
        var (parent, _, data) = SavedResultTestHarness.Repositories();
        var result = await Invoke<CommandResult<SavedCollectionView>>(parent, "SaveGraphAsync", new MemoryCollectionRequest(1, 10, "saved", [new("a", "updated"), new("new", "added")]));
        result.Status.Should().Be(CommandStatus.Applied); result.Value!.Name.Should().Be("saved");
        result.Value.Children.Should().Equal("updated", "added"); result.Value.InputName.Should().Be("saved");
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Single(row => row.Hidden).Text.Should().Be("hidden");
        data.PendingCount.Should().Be(0);
        ((List<string>)result.Value.Children).Clear();
        MemoryCollectionTestHarness.ChildStore(data).Count.Should().Be(4);
    }

    [Fact]
    public async Task AScalarPatchResultReadsChildrenIncludedInTheSharedSave()
    {
        var (parent, child, _) = SavedResultTestHarness.Repositories();
        await Invoke<CommandResult>(child, "StageTextAsync", 1);
        var result = await Invoke<CommandResult<SavedCollectionView>>(parent, "RenameAsync", new MemoryCollectionRequest(1, 10, "renamed", []));
        result.Value!.Children.Should().Equal("shared", "B"); result.Value.Name.Should().Be("renamed");
    }

    [Fact]
    public async Task CreatedRowsCanReadDeclaredSavedReferenceTargetsAndGeneratedKeys()
    {
        var (_, child, data) = SavedResultTestHarness.Repositories();
        var result = await Invoke<CommandResult<string>>(child, "CreateChildAsync", new MemoryCollectionRequest(1, 10, "created", []));
        result.Value.Should().Be("old");
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Single(row => row.Key == "created").Id.Should().Be(5);
    }

    [Fact]
    public async Task ResultsUseTheSavedPrimaryKeyInsteadOfReapplyingTheMutationPredicate()
    {
        var (parent, _, _) = SavedResultTestHarness.Repositories();
        (await Invoke<CommandResult<string>>(parent, "ChangePredicateAsync", 1)).Value.Should().Be("changed");
    }

    [Theory]
    [InlineData(999, 10, 1, CommandStatus.NotFound)]
    [InlineData(1, 99, 1, CommandStatus.NotFound)]
    [InlineData(1, 10, 99, CommandStatus.Conflict)]
    public async Task FailedSelectionOrExpectedVersionNeverRunsTheResultQuery(int id, int owner, int version, CommandStatus status)
    {
        var (parent, _, data) = SavedResultTestHarness.Repositories();
        var result = await Invoke<CommandResult<SavedCollectionView>>(parent, "RenameAsync", new MemoryCollectionRequest(id, owner, "ignored", [], version));
        result.Status.Should().Be(status); result.HasValue.Should().BeFalse(); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ResultFailureKeepsSavedRowsAndCheckpointRollbackCanStillRestoreThem()
    {
        var (parent, _, data) = SavedResultTestHarness.Repositories();
        await using (await data.BeginTransactionAsync())
        {
            Func<Task> execute = async () => await Invoke<CommandResult<SavedResultRejectedView>>(parent, "FailResultAsync", 1);
            (await execute.Should().ThrowAsync<RepositorySavedResultException>()).Which.InnerException.Should().BeOfType<InvalidOperationException>();
            MemoryCollectionTestHarness.ParentStore(data).Snapshot().Single(row => row.Id == 1).Name.Should().Be("committed");
            data.PendingCount.Should().Be(0);
        }
        MemoryCollectionTestHarness.ParentStore(data).Snapshot().Single(row => row.Id == 1).Name.Should().Be("old");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootVisibilityAppliesToSavedResultsUnlessDeliberatelyBypassed(bool bypass)
    {
        var (_, child, data) = SavedResultTestHarness.Repositories();
        if (bypass) (await Invoke<CommandResult<string>>(child, "BypassHideAsync", 1)).Value.Should().Be("A");
        else
        {
            Func<Task> execute = async () => await Invoke<CommandResult<string>>(child, "HideAsync", 1);
            await execute.Should().ThrowAsync<RepositorySavedResultException>();
        }
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Single(row => row.Id == 1).Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task TypedScriptsStillOverrideAutomaticSavedResultReads()
    {
        var (parent, _, data) = SavedResultTestHarness.Repositories();
        var expected = new CommandResult<SavedCollectionView>(CommandStatus.Applied, 0, new(1, "scripted", 1, "scripted", []), true);
        ScriptedTestDoubleTestHarness.Returns(parent, "OnRenameAsync", expected);
        (await Invoke<CommandResult<SavedCollectionView>>(parent, "RenameAsync", new MemoryCollectionRequest(1, 10, "ignored", []))).Should().Be(expected);
        MemoryCollectionTestHarness.ParentStore(data).Snapshot().Single(row => row.Id == 1).Name.Should().Be("old");
    }

    [Fact]
    public async Task InvalidRequiredFixtureRelationshipIsAResultFailureNotASimulatedSqlConstraint()
    {
        var (_, child, data) = SavedResultTestHarness.Repositories();
        Func<Task> execute = async () => await Invoke<CommandResult<string>>(child, "CreateChildAsync", new MemoryCollectionRequest(999, 10, "orphan", []));
        var exception = (await execute.Should().ThrowAsync<RepositorySavedResultException>()).Which;
        exception.InnerException!.Message.Should().Contain("required reference has no matching target");
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Single(row => row.Key == "orphan").ParentId.Should().Be(999);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task PreCancelledCallsNeverSaveOrQueryTheResult()
    {
        var (parent, _, data) = SavedResultTestHarness.Repositories(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var task = (Task<CommandResult<SavedCollectionView>>)parent.GetType().GetMethod("RenameAsync")!.Invoke(parent,
            [new MemoryCollectionRequest(1, 10, "cancelled", []), cancellation.Token])!;
        Func<Task> execute = async () => await task;
        await execute.Should().ThrowAsync<OperationCanceledException>();
        MemoryCollectionTestHarness.ParentStore(data).Snapshot().Single(row => row.Id == 1).Name.Should().Be("old"); data.PendingCount.Should().Be(0);
    }
}
