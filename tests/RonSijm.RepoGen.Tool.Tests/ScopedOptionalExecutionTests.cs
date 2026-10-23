using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScopedOptionalTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AbsentAndPresentNewReferencesPreserveSharedOwnershipAndImmutableMatches(bool mapped)
    {
        var (repository, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<ScopedOptionalView>>(repository, "ReplaceAsync", new ScopedOptionalRequest(1, 10, 1,
            [new(1, 1, 999, "partial ignored", null, null, "present retained"), new(1, 2, 999, "us", "b", 2, "absence retained"),
                new(1, 3, 10, null, null, null, "new absent"), new(1, 4, 10, "us", "b", 2, "new present")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(5);
        var rows = Rows(data, mapped);
        rows.Single(row => row.Scope == 1 && row.TargetId == 1).ReviewerCode.Should().Be("a");
        rows.Single(row => row.Scope == 1 && row.TargetId == 2).ReviewerRegion.Should().BeNull();
        rows.Single(row => row.Scope == 1 && row.TargetId == 2).OriginId.Should().BeNull();
        rows.Single(row => row.TargetId == 3).Should().Be(new ScopedOptionalData(1, 10, 1, 3, "eu", "a", null, null, null, "new absent"));
        (await Invoke<List<string>?>(repository, "NamesAsync", new ScopedOptionalRequest(1, 10, 1, [])))!.Should().Equal("Reviewer A:Origin A", "none:none", "none:none", "Reviewer B:Origin B");
        rows.Single(row => row.Hidden).Note.Should().Be("hidden"); rows.Single(row => row.Scope == 2).Note.Should().Be("other");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task PartialIndependentKeysFailBeforeMatchedChildVersionsAndRetainEarlierPendingWork(bool mapped, bool firstNull)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped); await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        Func<Task> fail = async () => await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1,
            [new(1, 1, 10, null, null, null, "stale", 99), new(1, 3, 10, firstNull ? null : "eu", firstNull ? "a" : null, null, "partial")]));
        await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*independent components entirely null or entirely present*");
        Rows(data, mapped).Should().Equal(baseline); data.PendingCount.Should().Be(1); (await data.SaveChangesAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AbsentReferencesStillRequireCorrectOwnerValuesBeforeBinding(bool mapped)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped);
        foreach (var operation in new[] { "StageAsync", "BadMapAsync" })
        {
            Func<Task> fail = async () => await Invoke<CommandResult>(repository, operation, new ScopedOptionalRequest(1, 10, 1,
                [new(1, 3, operation == "BadMapAsync" ? 10 : 20, null, null, null, "absent but wrong owner")]));
            await fail.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*before ownership*");
        }
        Rows(data, mapped).Should().Equal(baseline); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StageSharedSaveOmittedCollectionsAndRollbackPreserveOptionalStates(bool mapped)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped); data.CaptureBaseline();
        (await Invoke<CommandResult>(repository, "OptionalAsync", new ScopedOptionalRequest(1, 10, 1, null, Apply: false))).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(1); data.RestoreBaseline();
        (await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 1, 999, "unused", null, 999, "staged"), new(1, 3, 10, null, null, 2, "origin only")]))).Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Should().Equal(baseline); await Invoke<CommandResult>(repository, "StageNameAsync", 2);
        (await data.SaveChangesAsync()).Should().Be(5); var saved = Rows(data, mapped);
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<ScopedOptionalView>>(repository, "ReplaceAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 2, 10, "us", "b", null, "reviewer only")], 2))).Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Should().Equal(saved); data.RestoreBaseline(); Rows(data, mapped).Should().Equal(baseline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectionVersionsMissingPoliciesAndLimitsRetainTheirExistingPrecedence(bool mapped)
    {
        var (repository, data) = Repositories(mapped); var baseline = Rows(data, mapped);
        (await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(99, 10, 1, [new(1, 3, 10, null, "partial", null, "unused")]))).Status.Should().Be(CommandStatus.NotFound);
        (await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 3, 10, null, "partial", null, "unused")], 99))).Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<ScopedOptionalView>>(repository, "MergeAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 1, 999, null, "partial unused", null, "stale", 99)]))).Status.Should().Be(CommandStatus.Conflict);
        Func<Task> reject = async () => await Invoke<CommandResult<ScopedOptionalView>>(repository, "RejectAsync", new ScopedOptionalRequest(1, 10, 1, []));
        await reject.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        Func<Task> excessive = async () => await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1,
            Enumerable.Range(1, 6).Select(id => new ScopedOptionalItem(1, id, 10, null, null, null, "many")).ToArray()));
        await excessive.Should().ThrowAsync<ArgumentException>(); Rows(data, mapped).Should().Equal(baseline); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FixturesDoNotInventOptionalReferenceExistenceOrAuthorization()
    {
        var (repository, data) = ScopedOptionalTestHarness.Repositories();
        await Invoke<CommandResult>(repository, "StageAsync", new ScopedOptionalRequest(1, 10, 1, [new(1, 3, 10, "unknown", "unknown", 99, "explicit fixture")]));
        await data.SaveChangesAsync(); Rows(data, true).Single(row => row.TargetId == 3).OriginId.Should().Be(99);
        Store<ScopedOptionalReviewerData, int>(data, nameof(ScopedOptionalReviewer)).Count.Should().Be(3);
        Store<ScopedOptionalOriginData, (int, int)>(data, nameof(ScopedOptionalOrigin)).Count.Should().Be(3);
    }
}
