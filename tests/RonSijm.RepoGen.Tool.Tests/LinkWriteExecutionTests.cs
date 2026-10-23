using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.LinkWriteTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplacingLinksChangesPayloadAndMembershipNotEndpointsOrOtherOwners(bool mapped)
    {
        var (owner, target, data) = Repositories(mapped);
        var request = new LinkWriteRequest(1, 1, [new("eu", "a", "updated"), new("us", "a", "new")]);
        var result = await Invoke<CommandResult<LinkWriteView>>(owner, "ReplaceAsync", request);
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(4);
        var value = result.Value!;
        value.Version.Should().Be(2); value.Tags.Should().Equal("A", "Hidden target", "US A");
        var rows = Rows(data, mapped); rows.Should().HaveCount(4);
        rows.Single(row => row.OwnerTenantId == 2).Note.Should().Be("other");
        rows.Single(row => row.Hidden).Note.Should().Be("hidden");
        rows.Single(row => row.OwnerTenantId == 1 && row.TargetCode == "a" && row.TargetRegion == "eu").Version.Should().Be(2);
        (await Invoke<IReadOnlyList<int>>(target, "UsageAsync")).Should().Equal(2, 0, 1);
        var targets = mapped ? TargetStore(data).Snapshot().Select(row => row.Name).ToArray() : Store<LinkWriteTarget, (string, string)>(data, nameof(LinkWriteTarget)).Snapshot().Select(row => row.Name).ToArray();
        targets.Should().BeEquivalentTo(Targets().Select(row => row.Name)); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StageAndSharedSaveCheckpointAndBaselineRetainExistingSemantics(bool mapped)
    {
        var (owner, _, data) = Repositories(mapped); data.CaptureBaseline();
        var request = new LinkWriteRequest(1, 1, [new("us", "a", "new")]);
        (await Invoke<CommandResult>(owner, "StageAsync", request)).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Where(row => row.OwnerTenantId == 1 && !row.Hidden).Should().HaveCount(2);
        (await Invoke<CommandResult>(owner, "StageNameAsync", 2)).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(5);
        Rows(data, mapped).Where(row => row.OwnerTenantId == 1 && !row.Hidden).Should().ContainSingle().Which.TargetRegion.Should().Be("us");
        await using (await data.BeginTransactionAsync())
        {
            (await Invoke<CommandResult<LinkWriteView>>(owner, "ReplaceAsync", new LinkWriteRequest(1, 1, [new("eu", "b", "rollback")], 2))).Status.Should().Be(CommandStatus.Applied);
        }
        Rows(data, mapped).Where(row => row.OwnerTenantId == 1 && !row.Hidden).Should().ContainSingle().Which.TargetRegion.Should().Be("us");
        data.RestoreBaseline(); Rows(data, mapped).Should().HaveCount(4);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("link")]
    [InlineData("reject")]
    [InlineData("missing")]
    public async Task ConflictRejectAndMissingOutcomesLeaveAllStoresUntouched(string shape)
    {
        var (owner, _, data) = LinkWriteTestHarness.Repositories(); var before = Rows(data, true);
        var request = new LinkWriteRequest(shape == "missing" ? 99 : 1, 1, [new("eu", "a", "changed", shape == "link" ? 99 : 1)], shape == "parent" ? 99 : 1);
        if (shape == "reject")
        {
            Func<Task> reject = async () => await Invoke<CommandResult<LinkWriteView>>(owner, "RejectAsync", request);
            await reject.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        }
        else (await Invoke<CommandResult<LinkWriteView>>(owner, "ReplaceAsync", request)).Status.Should().Be(shape == "missing" ? CommandStatus.NotFound : CommandStatus.Conflict);
        Rows(data, true).Should().Equal(before); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task OmittedEmptyAndNullCollectionsAreNotConflated()
    {
        var (owner, _, data) = LinkWriteTestHarness.Repositories();
        (await Invoke<CommandResult>(owner, "OptionalAsync", new LinkWriteOptionalRequest(1, 1, default))).Status.Should().Be(CommandStatus.NoChanges);
        Func<Task> nullItems = async () => await Invoke<CommandResult>(owner, "OptionalAsync", new LinkWriteOptionalRequest(1, 1, new Optional<IReadOnlyList<LinkWriteItem>?>(null)));
        await nullItems.Should().ThrowAsync<ArgumentNullException>();
        (await Invoke<CommandResult>(owner, "OptionalAsync", new LinkWriteOptionalRequest(1, 1, new Optional<IReadOnlyList<LinkWriteItem>?>([])))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, true).Where(row => row.OwnerTenantId == 1).Should().ContainSingle().Which.Hidden.Should().BeTrue();
        TargetStore(data).Count.Should().Be(4);
    }

    [Fact]
    public async Task HiddenReferenceTargetsAreNotAnImplicitAuthorizationPolicy()
    {
        var (owner, _, data) = LinkWriteTestHarness.Repositories();
        var result = await Invoke<CommandResult<LinkWriteView>>(owner, "MergeAsync", new LinkWriteRequest(2, 1, [new("eu", "h", "linked")]));
        result.Status.Should().Be(CommandStatus.Applied);
        LinkStore(data).Find((2, 1, "eu", "h"))!.Note.Should().Be("linked");
        TargetStore(data).Find(("eu", "h"))!.Hidden.Should().BeTrue();
    }
}
