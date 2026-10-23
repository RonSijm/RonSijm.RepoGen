using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OverlappingReferenceTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreationChecksOwnerColumnsWhileMatchedAdditionalInputsStayIgnored(bool mapped)
    {
        var (repository, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<OverlappingReferenceView>>(repository, "ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1,
            [new(1, 1, 999, "missing", "missing", 999, "changed"), new(1, 3, 10, "b", "a", 2, "new")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(4);
        result.Value!.References.Should().Equal("Actor A:Actor B:Source A", "Actor B:Actor A:Source B");
        var rows = Rows(data, mapped);
        rows.Single(row => row.Scope == 1 && row.TargetId == 1).Should().Be(new OverlappingReferenceLinkData(1, 10, 1, 1, "a", "b", 1, "changed", 2));
        rows.Single(row => row.Scope == 1 && row.TargetId == 3).Zone.Should().Be(10);
        rows.Single(row => row.Scope == 2).Note.Should().Be("other"); rows.Single(row => row.Hidden).Note.Should().Be("hidden");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdditionalOwnerMismatchAndWrongMapperFailBeforeMatchedChildVersionsWithoutLosingEarlierWork(bool mapped)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped);
        await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        foreach (var operation in new[] { "StageAsync", "BadMapAsync" })
        {
            Func<Task> fail = async () => await Invoke<CommandResult>(repository, operation, new OverlappingReferenceRequest(1, 10, 1,
                [new(1, 1, 999, "ignored", "ignored", 999, "stale", 99), new(1, 3, operation == "BadMapAsync" ? 10 : 20, "a", "b", 1, "new")]));
            await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*before ownership*");
            Rows(data, mapped).Should().Equal(baseline); data.PendingCount.Should().Be(1);
        }
        (await data.SaveChangesAsync()).Should().Be(1); Rows(data, mapped).Should().Equal(baseline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StagingSharedSaveConditionalInputAndCheckpointPreserveAllReferences(bool mapped)
    {
        var (repository, data) = Repositories(mapped); data.CaptureBaseline(); var baseline = Rows(data, mapped);
        (await Invoke<CommandResult>(repository, "OptionalAsync", new OverlappingReferenceRequest(1, 10, 1, null, Apply: false))).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(1); // The parent version patch is independent of the omitted collection.
        data.RestoreBaseline();
        (await Invoke<CommandResult>(repository, "StageAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 3, 10, "b", "a", 2, "staged")]))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(baseline);
        await Invoke<CommandResult>(repository, "StageNameAsync", 2); (await data.SaveChangesAsync()).Should().Be(5);
        var saved = Rows(data, mapped);
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<OverlappingReferenceView>>(repository, "ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 2, 10, "a", "b", 1, "rollback")], 2))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Should().Equal(saved); data.RestoreBaseline(); Rows(data, mapped).Should().Equal(baseline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundsMissingPoliciesAndExpectedVersionsKeepExistingSafety(bool mapped)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped);
        Func<Task> reject = async () => await Invoke<CommandResult<OverlappingReferenceView>>(repository, "RejectAsync", new OverlappingReferenceRequest(1, 10, 1, []));
        await reject.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        (await Invoke<CommandResult<OverlappingReferenceView>>(repository, "MergeAsync", new OverlappingReferenceRequest(1, 10, 1, [new(1, 1, 10, "a", "b", 1, "stale", 99)]))).Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<OverlappingReferenceView>>(repository, "ReplaceAsync", new OverlappingReferenceRequest(1, 10, 1, [], 99))).Status.Should().Be(CommandStatus.Conflict);
        Func<Task> excessive = async () => await Invoke<CommandResult>(repository, "StageAsync", new OverlappingReferenceRequest(1, 10, 1,
            Enumerable.Range(1, 5).Select(id => new OverlappingReferenceItem(1, id, 10, "a", "b", 1, "many")).ToArray()));
        await excessive.Should().ThrowAsync<ArgumentException>(); Rows(data, mapped).Should().Equal(baseline); data.PendingCount.Should().Be(0);
    }
}
