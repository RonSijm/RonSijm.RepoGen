using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScalarEqualityTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MixedAssignmentsPersistOnlyDifferentFieldsUnlessVersionCheckingForcesAllAssignments(bool mapped, bool force)
    {
        var (repository, data) = Fixture(mapped);
        var result = await Invoke<CommandResult<ScalarEqualitySeed>>(repository, force ? "ForceMixedAsync" : "MixedAsync", new ScalarEqualityRequest(1, "ALPHA", "v1", Amount: 5));
        result.AffectedRows.Should().Be(1); result.Value!.Amount.Should().Be(5); result.Value.Label.Should().Be(force ? "ALPHA" : "Alpha");
        if (!force)
        {
            var batch = await Invoke<CommandResult<IReadOnlyList<ScalarEqualitySeed>>>(repository, "MixedManyAsync", (object)new ScalarEqualityRequest[] { new(2, "BETA", Amount: 6), new(1, "alpha", Amount: 7) });
            batch.Value!.Select(row => row.Label).Should().Equal("Beta", "Alpha");
        }
        Rows(data, mapped)[0].Label.Should().Be(force ? "ALPHA" : "Alpha");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReusedPendingEntriesRetainFieldPoliciesAndLaterForcedAssignments(bool mapped, bool force)
    {
        var (repository, data) = Fixture(mapped);
        await Invoke<CommandResult>(repository, "StageLabelAsync", new ScalarEqualityRequest(1, "ALPHA"));
        await Invoke<CommandResult>(repository, force ? "StageForceAsync" : "StageMixedAsync", new ScalarEqualityRequest(1, "alpha", "v1", Amount: 4));
        (await data.SaveChangesAsync()).Should().Be(1);
        Rows(data, mapped)[0].Label.Should().Be(force ? "alpha" : "Alpha"); Rows(data, mapped)[0].Amount.Should().Be(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentChangesRetainSavedValuesButQueriesKeepTheirOwnLiteralEquality(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        var equivalent = await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "SetLabelAsync", new ScalarEqualityRequest(1, "ALPHA"));
        equivalent.AffectedRows.Should().Be(0); equivalent.Value!.Label.Should().Be("Alpha"); Rows(data, mapped)[0].Label.Should().Be("Alpha");
        (await Invoke<IReadOnlyList<int>>(repository, "ByLabelAsync", "ALPHA")).Should().BeEmpty();
        (await Invoke<IReadOnlyList<int>>(repository, "ByLabelAsync", "Alpha")).Should().Equal(1);
        (await Invoke<CommandResult>(repository, "StageLabelAsync", new ScalarEqualityRequest(1, "alpha"))).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(0); data.PendingCount.Should().Be(0);
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "SetLabelAsync", new ScalarEqualityRequest(3, null))).AffectedRows.Should().Be(0);
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "SetLabelAsync", new ScalarEqualityRequest(3, "new"))).AffectedRows.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTokenEqualityWorksForSavedPatchesDeletesAndAtomicBoundedBatches(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        var renamed = await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "new", "v1", "V2"));
        renamed.Status.Should().Be(CommandStatus.Applied); renamed.Value!.Revision.Should().Be("V2");
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "stale", "v1"))).Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(4, "hidden", "v1"))).Status.Should().Be(CommandStatus.NotFound);
        var before = Rows(data, mapped);
        var conflict = await Invoke<CommandResult<IReadOnlyList<ScalarEqualitySeed>>>(repository, "RenameManyAsync", (object)new ScalarEqualityRequest[] { new(1, "must not save", "v2"), new(2, "stale", "v0") });
        conflict.Status.Should().Be(CommandStatus.Conflict); Rows(data, mapped).Should().Equal(before);
        var batch = await Invoke<CommandResult<IReadOnlyList<ScalarEqualitySeed>>>(repository, "RenameManyAsync", (object)new ScalarEqualityRequest[] { new(2, "second", "v1", "v2"), new(1, "first", "v2", "V3") });
        batch.Value!.Select(row => row.Id).Should().Equal(2, 1); batch.AffectedRows.Should().Be(2);
        (await Invoke<CommandResult<int>>(repository, "DeleteAsync", new ScalarEqualityRequest(1, "", "v3"))).Value.Should().Be(1);
        Rows(data, mapped).Should().NotContain(row => row.Id == 1); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckpointsBaselinesCancellationAndScriptsRetainTheirOriginalBoundaries(bool mapped)
    {
        var (repository, data) = Fixture(mapped); data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "temporary", "v1"));
            Rows(data, mapped)[0].Label.Should().Be("temporary");
        }
        Rows(data, mapped)[0].Label.Should().Be("Alpha");
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRenameAsync", new CommandResult<ScalarEqualitySeed>(CommandStatus.Conflict));
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "ignored", "v1"))).Status.Should().Be(CommandStatus.Conflict);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Func<Task> operation = () => (Task)repository.GetType().GetMethod("SetLabelAsync")!.Invoke(repository, [new ScalarEqualityRequest(1, "bad"), canceled.Token])!;
        await operation.Should().ThrowAsync<OperationCanceledException>();
        data.RestoreBaseline(); Rows(data, mapped)[0].Label.Should().Be("Alpha");
    }

    [Fact]
    public async Task ScalarPoliciesDoNotChooseOrChangeContextPartitionIdentity()
    {
        var assembly = Compile(Builder(mapped: true, keyed: true)); var (repository, data) = Fixture(true, assembly, "A");
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "saved", "v1"))).Status.Should().Be(CommandStatus.Applied);
        (await Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "missing", "v1", Partition: "a"))).Status.Should().Be(CommandStatus.NotFound);
        Rows(data, true)[0].Label.Should().Be("saved");
    }

    [Fact]
    public async Task DeclaredChildComparisonsApplyToPayloadStateWithoutChangingOwnerOrMatchingKeys()
    {
        var builder = SavedResultTestHarness.Builder();
        builder.Entity<MemoryCollectionChild>().HasInMemoryValueEquality(row => row.Text, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            .HasInMemoryValueEquality(row => row.Version, (left, right) => left == right);
        var (parent, child, data) = SavedResultTestHarness.Repositories(builder.Build());
        var result = await Invoke<CommandResult<SavedCollectionView>>(parent, "SaveGraphAsync", new MemoryCollectionRequest(1, 10, "updated", [new("a", "a"), new("b", "changed")]));
        result.AffectedRows.Should().Be(2); MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("A");
        MemoryCollectionTestHarness.ChildStore(data).Find(2)!.Text.Should().Be("changed");
        MemoryCollectionTestHarness.ChildStore(data).Find(4)!.ParentId.Should().Be(2);
        (await Invoke<CommandResult>(child, "StageTextAsync", 4)).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(1);
    }
}
