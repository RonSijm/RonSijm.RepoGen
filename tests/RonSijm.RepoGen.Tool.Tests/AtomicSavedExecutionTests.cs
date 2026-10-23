using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedExecutionTests
{
    [Fact]
    public async Task InsertProjectsGeneratedIdentityAndPreparedRootInSavedRelationshipCollection()
    {
        var (repository, _, data) = Fixture();
        var result = await Call<CommandResult<AtomicSavedView>>(repository, "Ensure", new AtomicSavedRequest(1, "c", "C"));
        result.Status.Should().Be(CommandStatus.Inserted);
        result.Value.Should().BeEquivalentTo(new AtomicSavedView(4, "C", 1, "saved parent", "C", ["A", "B", "C"]));
        MemoryCollectionTestHarness.ChildStore(data).Find(4)!.Text.Should().Be("C");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdatedResultsUseExistingPrimaryKeyAndFreshVersionNotCandidateOrOldPredicate()
    {
        var (repository, _, _) = Fixture();
        var result = await Call<CommandResult<AtomicSavedView>>(repository, "Put", new AtomicSavedRequest(1, "a", "changed"));
        result.Status.Should().Be(CommandStatus.Updated);
        result.Value.Should().BeEquivalentTo(new AtomicSavedView(1, "changed", 2, "saved parent", "changed", ["changed", "B"]));
    }

    [Theory]
    [InlineData("Ensure", "a", 1, CommandStatus.AlreadyExists)]
    [InlineData("Put", "a", 99, CommandStatus.NotMatched)]
    [InlineData("Put", "hidden", 1, CommandStatus.NotMatched)]
    public async Task NoOpOutcomesReturnNoExistingResult(string operation, string key, int version, CommandStatus status)
    {
        var (repository, _, data) = Fixture();
        var before = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        var result = await Call<CommandResult<AtomicSavedView>>(repository, operation, new AtomicSavedRequest(1, key, "changed", version));
        result.Status.Should().Be(status);
        result.HasValue.Should().BeFalse();
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ResultSnapshotExcludesPendingRootAndRelatedChangesAndDoesNotFlushThem()
    {
        var (repository, parent, data) = Fixture();
        await Call<CommandResult>(parent, "StageName", 1);
        await Call<CommandResult>(repository, "StageText", 2);
        var result = await Call<CommandResult<AtomicSavedView>>(repository, "Put", new AtomicSavedRequest(1, "a", "changed"));
        result.Value!.ParentName.Should().Be("saved parent");
        result.Value.Siblings.Should().Equal("changed", "B");
        data.PendingCount.Should().Be(2);
        await data.SaveChangesAsync();
        MemoryCollectionTestHarness.ParentStore(data).Find(1)!.Name.Should().Be("pending parent");
        MemoryCollectionTestHarness.ChildStore(data).Find(2)!.Text.Should().Be("pending child");
    }

    [Theory]
    [InlineData("FailEnsure", "new")]
    [InlineData("FailPut", "a")]
    public async Task ProjectionFailureLeavesRowsAndPendingStateUnchanged(string operation, string key)
    {
        var (repository, parent, data) = Fixture();
        await Call<CommandResult>(parent, "StageName", 1);
        var before = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        Func<Task> failure = () => Call<CommandResult<SavedResultRejectedView>>(repository, operation, new AtomicSavedRequest(1, key, "fail"));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("Saved DTO rejected: fail");
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Should().BeEquivalentTo(before);
        data.PendingCount.Should().Be(1);
    }

    [Theory]
    [InlineData("Ensure", "new")]
    [InlineData("Hide", "a")]
    public async Task FreshRootVisibilityFailurePublishesNothing(string operation, string key)
    {
        var (repository, _, data) = Fixture();
        var before = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        Func<Task> failure = () => Call<CommandResult<AtomicSavedView>>(repository, operation, new AtomicSavedRequest(1, key, "hidden", Hidden: true));
        await failure.Should().ThrowAsync<InvalidOperationException>();
        MemoryCollectionTestHarness.ChildStore(data).Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ExplicitVisibilityBypassAppliesToFreshRootButNotRelatedProjectionFilter()
    {
        var (repository, _, _) = Fixture();
        var result = await Call<CommandResult<AtomicSavedView>>(repository, "BypassHide", new AtomicSavedRequest(1, "a", "hidden", Hidden: true));
        result.Value!.Text.Should().Be("hidden");
        result.Value.Siblings.Should().Equal("B");
    }

    [Fact]
    public async Task SnapshotResultsAndCheckpointRollbackRemainDetached()
    {
        var (repository, _, data) = Fixture();
        data.CaptureBaseline();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            var result = await Call<CommandResult<AtomicSavedView>>(repository, "Ensure", new AtomicSavedRequest(1, "new", "new"));
            ((List<string>)result.Value!.Siblings).Clear();
            MemoryCollectionTestHarness.ChildStore(data).Count.Should().Be(4);
            await transaction.RollbackAsync();
        }
        MemoryCollectionTestHarness.ChildStore(data).Count.Should().Be(3);
        await Call<CommandResult<AtomicSavedView>>(repository, "Put", new AtomicSavedRequest(1, "a", "changed"));
        data.RestoreBaseline();
        MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("A");
    }
}
