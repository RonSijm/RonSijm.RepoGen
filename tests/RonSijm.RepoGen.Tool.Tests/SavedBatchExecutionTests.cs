using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchExecutionTests
{
    [Fact]
    public async Task FreshNestedResultsUseCompleteKeysAndRequestOrderAfterSharedSave()
    {
        var (parent, child, data) = SavedBatchTestHarness.Repositories();
        await Invoke<CommandResult>(child, "StageTextAsync", 1);
        var result = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "RefreshAsync",
            new SavedBatchRequest("report", [new("b", "second"), new("a", "first")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(3);
        var values = result.Value!;
        values.Select(row => row.Code).Should().Equal("b", "a"); values.Select(row => row.TenantId).Should().Equal(2, 1);
        values.Select(row => row.Name).Should().Equal("second", "first"); values.Select(row => row.Label).Should().OnlyContain(label => label == "report");
        values[1].Children.Should().Equal("shared save"); values[0].Children.Should().Equal("B");
        data.PendingCount.Should().Be(0);
        ((List<string>)values[1].Children).Clear(); SavedBatchTestHarness.ChildStore(data).Count.Should().Be(3);
    }

    [Theory]
    [InlineData("missing", 1, CommandStatus.NotFound)]
    [InlineData("hidden", 1, CommandStatus.NotFound)]
    [InlineData("b", 99, CommandStatus.Conflict)]
    public async Task MissingHiddenOrStaleTargetsDoNotPartiallySaveOrProject(string code, int version, CommandStatus expected)
    {
        var (parent, _, data) = SavedBatchTestHarness.Repositories();
        var result = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "RefreshAsync",
            new SavedBatchRequest("ignored", [new("a", "unexpected"), new(code, "unexpected", version)]));
        result.Status.Should().Be(expected); result.HasValue.Should().BeFalse(); data.PendingCount.Should().Be(0);
        SavedBatchTestHarness.ParentStore(data).Snapshot().Should().OnlyContain(row => row.Name == "old");
    }

    [Fact]
    public async Task EmptyAndUnassignedBatchesDoNotFlushOtherStaging()
    {
        var (parent, child, data) = SavedBatchTestHarness.Repositories(); await Invoke<CommandResult>(child, "StageTextAsync", 1);
        foreach (var items in new IReadOnlyList<SavedBatchItem>[] { [], [new("a", "ignored", Supplied: false)] })
        {
            var result = await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "UnversionedAsync", new SavedBatchRequest("empty", items));
            result.Status.Should().Be(CommandStatus.NoChanges); result.HasValue.Should().BeFalse();
        }
        data.PendingCount.Should().Be(1); SavedBatchTestHarness.ChildStore(data).Snapshot().Single(row => row.Id == 1).Text.Should().Be("A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostSaveVisibilityIsRecheckedUnlessExplicitlyBypassed(bool bypass)
    {
        var (parent, _, data) = SavedBatchTestHarness.Repositories(); var request = new SavedBatchRequest("hidden", [new("b", "saved"), new("a", "saved")]);
        if (bypass) (await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "BypassHideAsync", request)).Value!.Count.Should().Be(2);
        else
        {
            Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "HideAsync", request);
            await call.Should().ThrowAsync<RepositorySavedResultException>();
        }
        SavedBatchTestHarness.ParentStore(data).Snapshot().Should().OnlyContain(row => row.Hidden); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ProjectionFailureKeepsSharedSaveAndCheckpointRollbackRestoresAllStores()
    {
        var (parent, child, data) = SavedBatchTestHarness.Repositories();
        await using (await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult>(child, "StageTextAsync", 1);
            Func<Task> call = async () => await Invoke<CommandResult<IReadOnlyList<SavedResultRejectedView>>>(parent, "FailAsync",
                new SavedBatchRequest("fail", [new("b", "saved"), new("a", "saved")]));
            await call.Should().ThrowAsync<RepositorySavedResultException>();
            SavedBatchTestHarness.ParentStore(data).Snapshot().Where(row => !row.Hidden).Should().OnlyContain(row => row.Name == "saved");
            SavedBatchTestHarness.ChildStore(data).Snapshot().Single(row => row.Id == 1).Text.Should().Be("shared save");
        }
        SavedBatchTestHarness.ParentStore(data).Snapshot().Should().OnlyContain(row => row.Name == "old");
        SavedBatchTestHarness.ChildStore(data).Snapshot().Single(row => row.Id == 1).Text.Should().Be("A");
    }

    [Fact]
    public async Task ScriptsStillOverrideGeneratedBatchReadsWithoutChangingStores()
    {
        var (parent, _, data) = SavedBatchTestHarness.Repositories();
        var expected = new CommandResult<IReadOnlyList<SavedBatchView>>(CommandStatus.Applied, 0, [], true);
        ScriptedTestDoubleTestHarness.Returns(parent, "OnRefreshAsync", expected);
        (await Invoke<CommandResult<IReadOnlyList<SavedBatchView>>>(parent, "RefreshAsync", new SavedBatchRequest("script", [new("a", "ignored")]))).Should().Be(expected);
        SavedBatchTestHarness.ParentStore(data).Snapshot().Should().OnlyContain(row => row.Name == "old");
    }
}
