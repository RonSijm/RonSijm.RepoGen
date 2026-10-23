using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteMissingAndBoundsUseOnlyModelAndDesignerVisibleChildren(bool mapped)
    {
        var (parent, _, data) = Fixture(mapped);
        var result = await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "changed", [new("a", "updated")]));
        result.Status.Should().Be(CommandStatus.Applied);
        var rows = Rows(data, mapped); rows.Select(row => row.Id).Should().Equal(1, 3, 4, 5, 6);
        rows[0].Text.Should().Be("updated"); rows.Single(row => row.Id == 5).Text.Should().Be("deleted");
        rows.Single(row => row.Id == 3).Hidden.Should().BeTrue(); rows.Single(row => row.Id == 4).ParentId.Should().Be(2);
        // Parent filters are selection, not a stale-version result.
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(3, -1, "blocked", [], 99)))
            .Status.Should().Be(CommandStatus.NotFound); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RootBypassDoesNotBypassCollectionPoliciesAndInsertFactoriesSeeEverySavedRow(bool mapped)
    {
        var (parent, _, data) = Fixture(mapped);
        var result = await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "Bypass", new MemoryCollectionRequest(1, 10, "bypass", []));
        result.Status.Should().Be(CommandStatus.Applied);
        Rows(data, mapped).Select(row => row.Id).Should().Equal(3, 4, 5, 6);
        var input = new MemoryCollectionRequest(1, 10, "insert", [new("new", "deleted")], 2);
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", input)).Status.Should().Be(CommandStatus.Applied);
        var inserted = Rows(data, mapped).Single(row => row.Key == "new"); inserted.Id.Should().Be(7); inserted.Text.Should().Be("deleted");
        // A later operation still cannot select the newly filtered child for mutation.
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "empty", [], 3))).Succeeded.Should().BeTrue();
        Rows(data, mapped).Single(row => row.Key == "new").Should().Be(inserted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilterChangingUpdatesAreSavedAndFreshResultsHaveSeparateGraphSemantics(bool mapped)
    {
        var (parent, _, data) = Fixture(mapped);
        var result = await Invoke<Task<CommandResult<FilteredCollectionView>>>(parent, "SavedItems", new MemoryCollectionRequest(1, 10, "saved", [new("a", "deleted")]));
        result.Status.Should().Be(CommandStatus.Applied); result.Value!.Items.Should().Equal("hidden");
        Rows(data, mapped).Single(row => row.Id == 1).Text.Should().Be("deleted");
        // Unrelated child root visibility is not injected into the fresh collection projection.
        Rows(data, mapped).Single(row => row.Id == 3).Hidden.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StageSaveCheckpointsAndDirtyHiddenRowsKeepTheirExistingBoundaries(bool mapped)
    {
        var (parent, child, data) = Fixture(mapped); data.CaptureBaseline();
        (await Invoke<Task<CommandResult>>(parent, "StageItems", new MemoryCollectionRequest(1, 10, "staged", [new("a", "deleted")])))
            .Status.Should().Be(CommandStatus.Staged);
        Rows(data, mapped).Single(row => row.Id == 1).Text.Should().Be("A"); data.PendingCount.Should().Be(3);
        await data.SaveChangesAsync(); Rows(data, mapped).Single(row => row.Id == 1).Text.Should().Be("deleted");
        data.RestoreBaseline();
        (await Invoke<Task<CommandResult>>(child, "StageText", 3)).Status.Should().Be(CommandStatus.Staged);
        Func<Task> dirty = async () => await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "blocked", []));
        await dirty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*"); data.PendingCount.Should().Be(1);
        data.DiscardChanges();
        await using var transaction = await data.BeginTransactionAsync();
        await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "changed", []));
        await transaction.RollbackAsync(); Rows(data, mapped).Should().HaveCount(6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictsRejectMissingAndOmittedInputsDoNotStagePartialChanges(bool mapped)
    {
        var (parent, _, data) = Fixture(mapped);
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", new MemoryCollectionRequest(1, 10, "bad", [new("a", "bad", 99)])))
            .Status.Should().Be(CommandStatus.Conflict); data.PendingCount.Should().Be(0);
        Func<Task> reject = async () => await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "RejectItems", new MemoryCollectionRequest(1, 10, "bad", [new("a", "new")]));
        await reject.Should().ThrowAsync<ArgumentException>(); Rows(data, mapped).Should().HaveCount(6); data.PendingCount.Should().Be(0);
        var input = new MemoryCollectionOptionalRequest(1, default);
        (await Invoke<Task<CommandResult>>(parent, "Optional", input)).Status.Should().Be(CommandStatus.NoChanges);
        Rows(data, mapped).Should().HaveCount(6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsupportedUnarrangedCallsFailEvenOnEmptyStoresAndTypedScriptsRetainPriority(bool mapped)
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, Builder(mapped, filters: false, strict: false).Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); var (parent, _, data) = Fixture(mapped, assembly); data.Clear();
        var input = new MemoryCollectionRequest(1, 10, "empty", []);
        Func<Task> call = async () => await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", input);
        await call.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        var expected = new CommandResult<MemoryCollectionView>(CommandStatus.Applied, 17, new(99, "scripted", 1), hasValue: true);
        ScriptedTestDoubleTestHarness.Returns(parent, "OnSaveItemsAsync", expected);
        (await Invoke<Task<CommandResult<MemoryCollectionView>>>(parent, "SaveItems", input)).Should().BeSameAs(expected);
        data.PendingCount.Should().Be(0);
    }
}
