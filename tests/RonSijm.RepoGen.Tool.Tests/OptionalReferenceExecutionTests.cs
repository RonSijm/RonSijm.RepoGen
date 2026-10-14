using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OptionalReferenceTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewLinksAcceptAbsentOrCompleteReferencesWhileMatchesRetainPresenceAndValues(bool mapped)
    {
        var (repository, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1,
            [new(1, null, "ignored partial Add-only value", null, "changed present"), new(2, 2, "b", 2, "changed absent"),
             new(3, null, null, null, "new absent"), new(4, 2, "b", 2, "new present")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(5);
        var rows = Rows(data, mapped); var selected = rows.Where(row => row.OwnerId == 1 && !row.Hidden).ToArray();
        selected.Should().HaveCount(4); selected[0].ReviewerRegion.Should().Be(1); selected[0].SourceId.Should().Be(1);
        selected[1].ReviewerRegion.Should().BeNull(); selected[1].ReviewerCode.Should().BeNull(); selected[1].SourceId.Should().BeNull();
        selected[2].ReviewerRegion.Should().BeNull(); selected[2].ReviewerCode.Should().BeNull(); selected[2].SourceId.Should().BeNull();
        selected[3].ReviewerRegion.Should().Be(2); selected[3].ReviewerCode.Should().Be("b"); selected[3].SourceId.Should().Be(2);
        result.Value!.Links.Should().Equal(selected); rows.Single(row => row.Hidden).Note.Should().Be("hidden"); rows.Single(row => row.OwnerId == 2).Note.Should().Be("other");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PartialCompositeNewReferencesRestoreEarlierRowsAndUnrelatedPendingWork(bool mapped, bool firstNull)
    {
        var (repository, data) = Repositories(mapped); var before = Rows(data, mapped);
        await Invoke<CommandResult>(repository, "StageNameAsync", 2); var pending = data.PendingCount;
        Func<Task> fail = async () => await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1,
            [new(1, null, null, null, "must not persist"), new(3, null, null, null, "earlier valid addition"),
             new(4, firstNull ? null : 2, firstNull ? "b" : null, null, "partial")]));
        await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*Reviewer*entirely null or entirely present*");
        Rows(data, mapped).Should().Equal(before); data.PendingCount.Should().Be(pending);
        (await data.SaveChangesAsync()).Should().Be(1); Rows(data, mapped).Should().Equal(before);
        (await Invoke<OptionalReferenceView?>(repository, "FindAsync", 1))!.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalReferencesParticipateInStageSharedSaveCheckpointRollbackAndBaselines(bool mapped)
    {
        var (repository, data) = Repositories(mapped); data.CaptureBaseline(); var before = Rows(data, mapped);
        (await Invoke<CommandResult>(repository, "StageAsync", new OptionalReferenceRequest(1, [new(3, null, null, null, "staged")]))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(before); await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        (await data.SaveChangesAsync()).Should().Be(5); var saved = Rows(data, mapped);
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<OptionalReferenceView>>(repository, "ReplaceAsync", new OptionalReferenceRequest(1, [new(4, 2, "b", 2, "rollback")], 2))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Should().Equal(saved); data.RestoreBaseline(); Rows(data, mapped).Should().Equal(before);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ownerVersion")]
    [InlineData("linkVersion")]
    public async Task ExistingVersionAndMissingPoliciesStillPrecedeWrites(string invalid)
    {
        var (repository, data) = OptionalReferenceTestHarness.Repositories(); var before = Rows(data, true);
        var result = await Invoke<CommandResult<OptionalReferenceView>>(repository, "MergeAsync", new OptionalReferenceRequest(invalid == "missing" ? 99 : 1,
            [new(1, null, "unused partial", null, "changed", invalid == "linkVersion" ? 99 : 1)], invalid == "ownerVersion" ? 99 : 1));
        result.Status.Should().Be(invalid == "missing" ? CommandStatus.NotFound : CommandStatus.Conflict);
        Rows(data, true).Should().Equal(before); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CompleteNonexistentReferencesDoNotBecomeFabricatedFixtureForeignKeyChecks()
    {
        var (repository, data) = OptionalReferenceTestHarness.Repositories();
        await Invoke<CommandResult>(repository, "StageAsync", new OptionalReferenceRequest(1, [new(3, 99, "unknown", 99, "explicit fixture") ]));
        (await data.SaveChangesAsync()).Should().Be(4);
        Rows(data, true).Single(row => row.OwnerId == 1 && row.TargetId == 3).SourceId.Should().Be(99);
        Store<OptionalReferenceReviewerData, int>(data, nameof(OptionalReferenceReviewer)).Count.Should().Be(2);
        Store<OptionalReferenceSourceData, int>(data, nameof(OptionalReferenceSource)).Count.Should().Be(2);
    }
}
