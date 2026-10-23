using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SharedLinkTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidReferencesChangeOnlyVisibleOwnerScopedJoinRows(bool mapped)
    {
        var (repository, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 1, "changed"), new(1, 3, "new")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(4);
        result.Value!.Version.Should().Be(2); result.Value.Notes.Should().Equal("changed", "new");
        var rows = Rows(data, mapped); rows.Should().HaveCount(4);
        rows.Single(row => row.Scope == 2).Note.Should().Be("other");
        rows.Single(row => row.Hidden).Note.Should().Be("hidden");
        rows.Single(row => row.Scope == 1 && row.TargetId == 1).Version.Should().Be(2);
        (mapped ? Store<SharedLinkTargetData, (int, int)>(data, nameof(SharedLinkTarget)).Count : Store<SharedLinkTarget, (int, int)>(data, nameof(SharedLinkTarget)).Count).Should().Be(5);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StagingSharedSavingCheckpointAndBaselineKeepTheirExistingBehavior(bool mapped)
    {
        var (repository, data) = Repositories(mapped); data.CaptureBaseline();
        var original = Rows(data, mapped);
        (await Invoke<CommandResult>(repository, "StageAsync", new SharedLinkRequest(1, 1, [new(1, 3, "staged")]))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(original);
        (await Invoke<CommandResult>(repository, "StageNameAsync", 2)).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(5);
        var saved = Rows(data, mapped);
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 2, "rollback")], 2))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Should().Equal(saved);
        data.RestoreBaseline(); Rows(data, mapped).Should().Equal(original);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CrossOwnerKeysAndBadMappingsCannotDiscardEarlierPendingWork(bool mapped, bool mapper)
    {
        var (repository, data) = Repositories(mapped); var original = Rows(data, mapped);
        await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        Func<Task> fail = mapper
            ? async () => await Invoke<CommandResult>(repository, "BadMapAsync", new SharedLinkRequest(1, 1, [new(1, 3, "bad map")]))
            : async () => await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", new SharedLinkRequest(1, 1, [new(1, 1, "valid"), new(2, 1, "wrong owner")]));
        if (mapper)
        {
            await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("*supplied reference key*");
        }
        else await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
        Rows(data, mapped).Should().Equal(original); data.PendingCount.Should().Be(1);
        (await Invoke<SharedLinkView?>(repository, "FindAsync", new SharedLinkRequest(1, 1, [])))!.Version.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("link")]
    [InlineData("missing")]
    [InlineData("reject")]
    public async Task ExistingConflictMissingAndRejectPoliciesStillApply(string kind)
    {
        var (repository, data) = SharedLinkTestHarness.Repositories(); var before = Rows(data, true);
        var request = new SharedLinkRequest(kind == "missing" ? 99 : 1, 1, [new(1, 1, "changed", kind == "link" ? 99 : 1)], kind == "parent" ? 99 : 1);
        if (kind == "reject")
        {
            Func<Task> fail = async () => await Invoke<CommandResult<SharedLinkView>>(repository, "RejectAsync", request);
            await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        }
        else (await Invoke<CommandResult<SharedLinkView>>(repository, "ReplaceAsync", request)).Status.Should().Be(kind == "missing" ? CommandStatus.NotFound : CommandStatus.Conflict);
        Rows(data, true).Should().Equal(before); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task OmittedInputsAreNotValidatedAndExplicitNullIsNotDeleteAll()
    {
        var (repository, data) = SharedLinkTestHarness.Repositories(); var before = Rows(data, true);
        (await Invoke<CommandResult>(repository, "OptionalAsync", new SharedLinkRequest(1, 1, [new(2, 1, "ignored")], Apply: false))).Status.Should().Be(CommandStatus.NoChanges);
        Func<Task> nullInput = async () => await Invoke<CommandResult>(repository, "OptionalAsync", new SharedLinkRequest(1, 1, null));
        await nullInput.Should().ThrowAsync<ArgumentNullException>();
        Rows(data, true).Should().Equal(before);
        (await Invoke<CommandResult>(repository, "OptionalAsync", new SharedLinkRequest(1, 1, []))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, true).Where(row => row.Scope == 1).Should().ContainSingle().Which.Hidden.Should().BeTrue();
    }
}
