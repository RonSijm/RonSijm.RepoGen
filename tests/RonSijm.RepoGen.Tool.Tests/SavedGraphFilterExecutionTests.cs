using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SavedGraphFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedGraphFilterExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreshOptionalTargetsAndOrderedBatchesUsePostSaveFilteredGraphs(bool mapped)
    {
        var (rows, _, _) = Fixture(mapped);
        (await Call<CommandResult<FilteredNavigationCard>>(rows, "Rename", new SavedGraphFilterRequest(1, "renamed"))).Value.Should().Be(new FilteredNavigationCard(1, "renamed", 1));
        (await Call<CommandResult<FilteredNavigationCard>>(rows, "Rename", new SavedGraphFilterRequest(4, "archived target"))).Value!.VisibleFolderId.Should().BeNull();
        var batch = await Call<CommandResult<IReadOnlyList<FilteredNavigationCard>>>(rows, "RenameMany", (object)new SavedGraphFilterRequest[] { new(5, "unassigned"), new(1, "first"), new(4, "fourth") });
        batch.Value!.Select(item => item.Id).Should().Equal(5, 1, 4); batch.Value!.Select(item => item.VisibleFolderId).Should().Equal(null, 1, null);
        var created = await Call<CommandResult<IReadOnlyList<FilteredNavigationCard>>>(rows, "CreateMany", (object)new SavedGraphFilterRequest[] { new(8, "eight", 2), new(7, "seven", 1) });
        created.Value!.Select(item => item.Id).Should().Equal(8, 7); created.Value!.Select(item => item.VisibleFolderId).Should().Equal(null, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedSaveRequeriesRelatedFiltersAndDoesNotInjectRelatedRootVisibility(bool mapped)
    {
        var (rows, folders, data) = Fixture(mapped);
        var before = await Call<CommandResult<SavedGraphFilterCollectionCard>>(rows, "RenameGraph", new SavedGraphFilterRequest(1, "before shared save"));
        before.Value!.RelatedCount.Should().Be(2); // Deleted children disappear; related root visibility is not injected.
        (await Call<CommandResult>(folders, "StageArchive", 1)).Status.Should().Be(CommandStatus.Staged);
        var saved = await Call<CommandResult<SavedGraphFilterCollectionCard>>(rows, "RenameGraph", new SavedGraphFilterRequest(1, "shared"));
        saved.Value!.VisibleFolderId.Should().BeNull(); saved.Value!.RelatedCount.Should().Be(0); data.PendingCount.Should().Be(0);
        // A new owner can be correlated with explicitly seeded detached children; no graph is saved.
        if (mapped) ModelFilterTestHarness.Store<ModelFilterRowSeed>(data, "ModelFilterRow").SeedRange([new(10, "child", false, false, 6), new(11, "deleted", true, false, 6), new(12, "hidden", false, true, 6)]);
        else ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").SeedRange([new() { Id = 10, Title = "child", FolderId = 6 }, new() { Id = 11, Title = "deleted", Deleted = true, FolderId = 6 }, new() { Id = 12, Title = "hidden", Hidden = true, FolderId = 6 }]);
        var collection = (await Call<CommandResult<FilteredNavigationFolderCard>>(folders, "Create", new SavedGraphFilterFolderInput(6))).Value!;
        collection.Count.Should().Be(2); collection.Titles.Should().Equal("child", "hidden");
        collection.Titles.Clear();
        (await Call<IReadOnlyList<ModelFilterRowSeed>>(rows, "All")).Should().HaveCount(8);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HiddenSavedRootsFailAfterPersistenceAndTransactionsCanRollBackWithoutShorteningBatches(bool mapped)
    {
        var (rows, _, data) = Fixture(mapped); data.CaptureBaseline();
        Func<Task> single = async () => await Call<CommandResult<FilteredNavigationCard>>(rows, "Archive", 1);
        await single.Should().ThrowAsync<RepositorySavedResultException>();
        (await Call<IReadOnlyList<ModelFilterRowSeed>>(rows, "All")).Single(item => item.Id == 1).Deleted.Should().BeTrue();
        data.RestoreBaseline();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            Func<Task> batch = async () => await Call<CommandResult<IReadOnlyList<FilteredNavigationCard>>>(rows, "ArchiveMany", (object)new SavedGraphFilterRequest[] { new(4, ""), new(1, "") });
            await batch.Should().ThrowAsync<RepositorySavedResultException>();
            (await Call<IReadOnlyList<ModelFilterRowSeed>>(rows, "All")).Count(item => item.Deleted).Should().Be(3);
            await transaction.RollbackAsync();
        }
        (await Call<IReadOnlyList<ModelFilterRowSeed>>(rows, "All")).Count(item => item.Deleted).Should().Be(1);
        Func<Task> create = async () => await Call<CommandResult<FilteredNavigationCard>>(rows, "Create", new SavedGraphFilterRequest(9, "hidden by model", 1, Deleted: true));
        await create.Should().ThrowAsync<RepositorySavedResultException>();
        (await Call<IReadOnlyList<ModelFilterRowSeed>>(rows, "All")).Should().Contain(item => item.Id == 9 && item.Deleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VisibilityMissingOutcomesBoundsAndCancellationRemainIndependent(bool mapped)
    {
        var (rows, _, data) = Fixture(mapped);
        (await Call<CommandResult<FilteredNavigationCard>>(rows, "Rename", new SavedGraphFilterRequest(2, "deleted"))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult<FilteredNavigationCard>>(rows, "Rename", new SavedGraphFilterRequest(3, "hidden"))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult<FilteredNavigationCard>>(rows, "RenameBypass", new SavedGraphFilterRequest(3, "bypassed"))).Value!.Title.Should().Be("bypassed");
        Func<Task> bounds = async () => await Call<CommandResult<IReadOnlyList<FilteredNavigationCard>>>(rows, "RenameMany", (object)new SavedGraphFilterRequest[] { new(1, "a"), new(4, "b"), new(5, "c"), new(3, "d") });
        await bounds.Should().ThrowAsync<ArgumentException>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> cancelled = async () => await (Task<CommandResult<FilteredNavigationCard>>)rows.GetType().GetMethod("RenameAsync")!.Invoke(rows, [new SavedGraphFilterRequest(1, "cancelled"), cancellation.Token])!;
        await cancelled.Should().ThrowAsync<OperationCanceledException>(); data.PendingCount.Should().Be(0);
    }
}
