using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedIdsAreReservedAcrossTheBatchAndEarlierStagedRows(bool mapped)
    {
        var (repository, parent, data) = CreateManyTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(repository, "StageAsync", new CreateManyRequest([new(7, " staged ")]));
        await Invoke<CommandResult>(parent, "StageNameAsync", 7);
        data.PendingCount.Should().Be(2);
        var result = await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, " third "), new(7, "second")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(4); result.Value.Should().Equal(9, 10);
        CreateManyTestHarness.Snapshot(data, mapped).Select(row => row.Name).Should().Equal("old", "staged", "third", "second");
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshResultsUseOneSavedSnapshotAndIncludeUnrelatedSharedSaves(bool mapped)
    {
        var (repository, parent, data) = CreateManyTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(parent, "StageNameAsync", 7);
        var result = await Invoke<CommandResult<IReadOnlyList<CreateManyView>>>(repository, "FreshAsync", new CreateManyRequest([new(7, "b"), new(7, "a")], "label"));
        result.AffectedRows.Should().Be(3);
        result.Value.Should().Equal(new CreateManyView(8, "b", "fixture", "shared", "label"), new CreateManyView(9, "a", "fixture", "shared", "label"));
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastMappingFailureLeavesEarlierPendingWorkAndDoesNotConsumeIds(bool mapped)
    {
        var (repository, parent, data) = CreateManyTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(parent, "StageNameAsync", 7);
        Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "valid"), new(7, null!)]));
        await call.Should().ThrowAsync<NullReferenceException>();
        data.PendingCount.Should().Be(1); CreateManyTestHarness.Snapshot(data, mapped).Should().HaveCount(1);
        (await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "next")]))).Value.Should().Equal(8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndOversizeInputsNeverFlushOtherPendingWork(bool mapped)
    {
        var (repository, parent, data) = CreateManyTestHarness.Fixture(mapped);
        await Invoke<CommandResult>(parent, "StageNameAsync", 7);
        (await Invoke<CommandResult>(repository, "CountAsync", new CreateManyRequest([]))).Status.Should().Be(CommandStatus.NoChanges);
        Func<Task> call = async () => await Invoke<CommandResult>(repository, "CountAsync", new CreateManyRequest(Enumerable.Repeat(new CreateManyItem(7, "x"), 4).ToArray()));
        await call.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(1);
        CreateManyTestHarness.ParentStore(data).Find(7)!.Name.Should().Be("old");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibilityAndProjectionFailuresAreReportedAfterSaving(bool mapped)
    {
        var (repository, _, data) = CreateManyTestHarness.Fixture(mapped);
        Func<Task> hidden = async () => await Invoke<CommandResult<IReadOnlyList<CreateManyView>>>(repository, "FreshAsync", new CreateManyRequest([new(7, "hidden", true)]));
        await hidden.Should().ThrowAsync<RepositorySavedResultException>();
        CreateManyTestHarness.Snapshot(data, mapped).Should().Contain(row => row.Name == "hidden");
        var bypass = await Invoke<CommandResult<IReadOnlyList<CreateManyView>>>(repository, "BypassAsync", new CreateManyRequest([new(7, "bypass", true)]));
        bypass.Value![0].Name.Should().Be("bypass");
        Func<Task> rejected = async () => await Invoke<CommandResult<IReadOnlyList<SavedResultRejectedView>>>(repository, "FailAsync", new CreateManyRequest([new(7, "saved")]));
        await rejected.Should().ThrowAsync<RepositorySavedResultException>(); data.PendingCount.Should().Be(0);
        CreateManyTestHarness.Snapshot(data, mapped).Should().Contain(row => row.Name == "saved");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckpointAndBaselineRestorePermitDeterministicAllocationReplay(bool mapped)
    {
        var (repository, _, data) = CreateManyTestHarness.Fixture(mapped); data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
            (await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "one"), new(7, "two")]))).Value.Should().Equal(8, 9);
        CreateManyTestHarness.Snapshot(data, mapped).Should().HaveCount(1);
        (await Invoke<CommandResult<IReadOnlyList<int>>>(repository, "CreateAsync", new CreateManyRequest([new(7, "replay")]))).Value.Should().Equal(8);
        data.RestoreBaseline(); CreateManyTestHarness.Snapshot(data, mapped).Should().HaveCount(1);
    }
}
