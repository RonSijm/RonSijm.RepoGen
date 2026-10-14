using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryCollectionTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionExecutionTests
{
    [Fact]
    public async Task GeneratedCommandReusesDesignedRulesAndChildFactoriesWithoutLoadingSeededGraphs()
    {
        var (parent, child, data) = MemoryCollectionTestHarness.Repositories();
        var result = await Invoke<CommandResult<MemoryCollectionView>>(parent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "saved", [new("a", "updated"), new("c", "new"), new("d", "newer")]));
        result.AffectedRows.Should().Be(5); result.Value.Should().Be(new MemoryCollectionView(1, "saved", 2));
        (await Invoke<IReadOnlyList<string>>(child, "ListAsync", 1)).Should().Equal("updated", "new", "newer");
        ChildStore(data).Find(3)!.Hidden.Should().BeTrue(); ChildStore(data).Find(4)!.ParentId.Should().Be(2);
        ChildStore(data).Find(5)!.ParentId.Should().Be(1); ChildStore(data).Find(6)!.ParentId.Should().Be(1);
        ParentStore(data).Find(1)!.Children.Should().BeEmpty("fixture navigation graphs are not persisted or used as mutation scope");
    }

    [Fact]
    public async Task StageAndSaveShareChildRowsAndKeepSavedQueriesUnchangedUntilSave()
    {
        var (parent, child, data) = MemoryCollectionTestHarness.Repositories();
        await Invoke<CommandResult>(parent, "StageItemsAsync", new MemoryCollectionRequest(1, 10, "saved", [new("a", "new A")]));
        (await Invoke<IReadOnlyList<string>>(child, "ListAsync", 1)).Should().Equal("old A", "old B");
        data.PendingCount.Should().Be(3); (await data.SaveChangesAsync()).Should().Be(3);
        (await Invoke<IReadOnlyList<string>>(child, "ListAsync", 1)).Should().Equal("new A");
    }

    [Theory]
    [InlineData(0, 10, 1, 1, CommandStatus.NotFound)]
    [InlineData(1, 99, 1, 1, CommandStatus.NotFound)]
    [InlineData(1, 10, 99, 1, CommandStatus.Conflict)]
    [InlineData(1, 10, 1, 99, CommandStatus.Conflict)]
    public async Task MissingOwnershipOrStaleVersionsNeverStageEarlierChanges(int id, int owner, int parentVersion, int childVersion, CommandStatus status)
    {
        var (parent, _, data) = MemoryCollectionTestHarness.Repositories();
        var result = await Invoke<CommandResult<MemoryCollectionView>>(parent, "SaveItemsAsync", new MemoryCollectionRequest(id, owner, "new", [new("a", "first"), new("b", "stale", childVersion)], parentVersion));
        result.Status.Should().Be(status); data.PendingCount.Should().Be(0); ChildStore(data).Find(1)!.Text.Should().Be("old A");
    }

    [Theory]
    [InlineData("KeepItemsAsync")]
    [InlineData("RejectItemsAsync")]
    public async Task GeneratedMissingPoliciesAreNotInferred(string method)
    {
        var (parent, _, data) = MemoryCollectionTestHarness.Repositories();
        Func<Task> action = () => Invoke<CommandResult<MemoryCollectionView>>(parent, method, new MemoryCollectionRequest(1, 10, "new", [new("a", "new A")]));
        if (method.StartsWith("Reject", StringComparison.Ordinal)) { await action.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0); }
        else { await action(); ChildStore(data).Find(2).Should().NotBeNull(); }
    }

    [Fact]
    public async Task OptionalOmittedNullEmptyAndConditionalInputsStayDistinct()
    {
        var (parent, _, data) = MemoryCollectionTestHarness.Repositories();
        (await Invoke<CommandResult>(parent, "OptionalItemsAsync", new MemoryCollectionOptionalRequest(1, default))).Status.Should().Be(CommandStatus.NoChanges);
        Func<Task> nullInput = () => Invoke<CommandResult>(parent, "OptionalItemsAsync", new MemoryCollectionOptionalRequest(1, new Optional<IReadOnlyList<MemoryCollectionItem>?>(null)));
        await nullInput.Should().ThrowAsync<ArgumentNullException>();
        var unsupplied = new Optional<IReadOnlyList<MemoryCollectionItem>?>([new("a", "ignored", Supplied: false), new("b", "ignored", Supplied: false)]);
        (await Invoke<CommandResult>(parent, "OptionalItemsAsync", new MemoryCollectionOptionalRequest(1, unsupplied))).Status.Should().Be(CommandStatus.NoChanges);
        var empty = new Optional<IReadOnlyList<MemoryCollectionItem>?>([]);
        (await Invoke<CommandResult>(parent, "OptionalItemsAsync", new MemoryCollectionOptionalRequest(1, empty))).AffectedRows.Should().Be(2);
        ChildStore(data).Count.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateAndOversizedInputFailBeforeParentSelection(bool oversized)
    {
        var (parent, _, data) = MemoryCollectionTestHarness.Repositories();
        var items = oversized ? new[] { new MemoryCollectionItem("1", ""), new("2", ""), new("3", ""), new("4", "") } : [new MemoryCollectionItem("a", ""), new("a", "")];
        Func<Task> action = () => Invoke<CommandResult<MemoryCollectionView>>(parent, "SaveItemsAsync", new MemoryCollectionRequest(99, 99, "", items));
        await action.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task DirtyChildrenAreNotMergedAndScriptOverridesStillWin()
    {
        var (parent, child, data) = MemoryCollectionTestHarness.Repositories();
        await Invoke<CommandResult>(child, "StageTextAsync", 1);
        Func<Task> action = () => Invoke<CommandResult<MemoryCollectionView>>(parent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "new", []));
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*"); data.PendingCount.Should().Be(1);
        var scripted = new CommandResult<MemoryCollectionView>(CommandStatus.Conflict);
        ScriptedTestDoubleTestHarness.Returns(parent, "OnSaveItemsAsync", scripted);
        (await Invoke<CommandResult<MemoryCollectionView>>(parent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "new", []))).Should().BeSameAs(scripted);
        data.PendingCount.Should().Be(1);
    }
}
