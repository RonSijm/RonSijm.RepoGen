using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.CompositeCollectionTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheWholeCombinationMatchesWhileCompleteOwnershipAndHiddenRowsRemainProtected(bool mapped)
    {
        var (parent, child, data) = Repositories(mapped);
        var result = await Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1,
            [new(99, "title", "en", "new English"), new(99, "title", "nl", "new Dutch"), new(6, "title", "fr", "new French")]));
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(5); result.Value.Should().Be(2);
        var items = await Invoke<IReadOnlyList<CompositeCollectionView>>(child, "ListAsync", new CompositeCollectionRequest(10, 1, []));
        items.Select(row => row.Id).Should().Equal(1, 6, 2);
        items.Select(row => row.Text).Should().Equal("new English", "new French", "new Dutch");
        (await Invoke<IReadOnlyList<CompositeCollectionView>>(child, "ListAsync", new CompositeCollectionRequest(20, 1, []))).Should()
            .Equal(new CompositeCollectionView(5, "title", "en", "other tenant", 1));
        if (mapped)
        {
            var rows = Store<CompositeCollectionChildData, int>(data);
            rows.Find(3).Should().BeNull(); rows.Find(4)!.Text.Should().Be("hidden");
            rows.Find(6)!.TenantId.Should().Be(10); rows.Find(6)!.ParentId.Should().Be(1);
        }
        else
        {
            var rows = Store<CompositeCollectionChild, int>(data);
            rows.Find(3).Should().BeNull(); rows.Find(4)!.Text.Should().Be("hidden");
            rows.Find(6)!.TenantId.Should().Be(10); rows.Find(6)!.ParentId.Should().Be(1);
            Store<CompositeCollectionParent, (int, int)>(data).Find((10, 1))!.Children.Should().BeEmpty();
        }
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData("MergeAsync")]
    [InlineData("RejectAsync")]
    public async Task MissingPoliciesUseCompleteKeysAndNeverInferDeletion(string method)
    {
        var (parent, child, data) = CompositeCollectionTestHarness.Repositories();
        Func<Task> run = () => Invoke<CommandResult<int>>(parent, method, new CompositeCollectionRequest(10, 1, [new(1, "title", "en", "updated")]));
        if (method == "RejectAsync") await run.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        else await run();
        (await Invoke<IReadOnlyList<CompositeCollectionView>>(child, "ListAsync", new CompositeCollectionRequest(10, 1, []))).Should().HaveCount(3);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nullCode")]
    [InlineData("nullLanguage")]
    [InlineData("nullItem")]
    [InlineData("tooMany")]
    public async Task InvalidInputDoesNotDiscardPriorUnrelatedStaging(string scenario)
    {
        var (parent, child, data) = CompositeCollectionTestHarness.Repositories();
        await Invoke<CommandResult>(child, "StageTextAsync", 5);
        IReadOnlyList<CompositeCollectionItem> items = scenario switch
        {
            "duplicate" => [new(1, "title", "en", "first"), new(2, "title", "en", "second")],
            "nullCode" => [new(1, null!, "en", "invalid")],
            "nullLanguage" => [new(1, "title", null!, "invalid")],
            "nullItem" => [null!],
            _ => Enumerable.Range(10, 6).Select(id => new CompositeCollectionItem(id, "title", id.ToString(System.Globalization.CultureInfo.InvariantCulture), "invalid")).ToList()
        };
        Func<Task> run = () => Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1, items));
        await run.Should().ThrowAsync<ArgumentException>();
        data.PendingCount.Should().Be(1);
        Store<CompositeCollectionParentData, (int, int)>(data).Find((10, 1))!.Version.Should().Be(1);
        Store<CompositeCollectionChildData, int>(data).Find(1)!.Text.Should().Be("old English");
        (await data.SaveChangesAsync()).Should().Be(1);
        Store<CompositeCollectionChildData, int>(data).Find(5)!.Text.Should().Be("shared");
    }

    [Fact]
    public async Task StagedCompositeReplacementsShareOneSaveAndCheckpointRollbackRestoresTheWholeFixture()
    {
        var (parent, child, data) = CompositeCollectionTestHarness.Repositories();
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult>(child, "StageTextAsync", 5);
            (await Invoke<CommandResult>(parent, "StageAsync", new CompositeCollectionRequest(10, 1, [new(1, "title", "en", "staged")]))).Status.Should().Be(CommandStatus.Staged);
            data.PendingCount.Should().Be(5);
            Store<CompositeCollectionChildData, int>(data).Find(2).Should().NotBeNull();
            Store<CompositeCollectionChildData, int>(data).Find(1)!.Text.Should().Be("old English");
            (await data.SaveChangesAsync()).Should().Be(5);
            Store<CompositeCollectionChildData, int>(data).Find(2).Should().BeNull();
        }
        Store<CompositeCollectionChildData, int>(data).Snapshot().Should().HaveCount(5);
        Store<CompositeCollectionChildData, int>(data).Find(1)!.Text.Should().Be("old English");
        Store<CompositeCollectionChildData, int>(data).Find(5)!.Text.Should().Be("other tenant");
        data.PendingCount.Should().Be(0);
        await Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1, []));
        data.RestoreBaseline();
        Store<CompositeCollectionChildData, int>(data).Snapshot().Should().HaveCount(5);
    }

    [Theory]
    [InlineData(99, 1)]
    [InlineData(1, 99)]
    public async Task ParentOrChildConflictsRestoreAllEarlierAssignments(int parentVersion, int childVersion)
    {
        var (parent, _, data) = CompositeCollectionTestHarness.Repositories();
        var result = await Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1,
            [new(1, "title", "en", "first"), new(2, "title", "nl", "conflict", childVersion)], parentVersion));
        result.Status.Should().Be(CommandStatus.Conflict);
        data.PendingCount.Should().Be(0); Store<CompositeCollectionChildData, int>(data).Find(1)!.Text.Should().Be("old English");
    }

    [Fact]
    public async Task DirtyTargetsRequireExplicitSaveAndTypedScriptsStillOverrideTheBackend()
    {
        var (parent, child, data) = CompositeCollectionTestHarness.Repositories();
        await Invoke<CommandResult>(child, "StageTextAsync", 1);
        Func<Task> run = () => Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1, []));
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*");
        var result = new CommandResult<int>(CommandStatus.Conflict);
        ScriptedTestDoubleTestHarness.Returns(parent, "OnReplaceAsync", result);
        (await Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1, []))).Should().BeSameAs(result);
        data.PendingCount.Should().Be(1);
    }
}
