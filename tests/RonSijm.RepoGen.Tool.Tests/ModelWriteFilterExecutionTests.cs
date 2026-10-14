using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using Writes = RonSijm.RepoGen.Tool.Tests.ModelWriteFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterExecutionTests
{
    private static Task<T> Call<T>(object repository, string name, params object?[] arguments)
        => MemoryWriteTestHarness.Invoke<T>(repository, name + "Async", arguments);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModelFilterPrecedesTokensAndRemainsIndependentOfVisibilityBypass(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped);
        foreach (var id in new[] { 2, 3 })
            (await Call<CommandResult<ModelWriteFilterCard>>(note, "Rename", new ModelWriteFilterPatch(id, "blocked", 99))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult<ModelWriteFilterCard>>(note, "RenameBypass", new ModelWriteFilterPatch(2, "blocked"))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult<ModelWriteFilterCard>>(note, "RenameBypass", new ModelWriteFilterPatch(3, "visible-by-policy"))).Value!.Title.Should().Be("visible-by-policy");
        (await Call<CommandResult<ModelWriteFilterCard>>(note, "Rename", new ModelWriteFilterPatch(1, "stale", 99))).Status.Should().Be(CommandStatus.Conflict);
        Writes.Rows(data, mapped)[0].Title.Should().Be("one"); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StagedSelectionUsesSavedRowsAndSharedSaveDoesNotTreatTheFilterAsAConstraint(bool mapped)
    {
        var (note, audit, data) = Writes.Fixture(mapped);
        (await Call<CommandResult>(note, "StageHide", 1)).Status.Should().Be(CommandStatus.Staged);
        (await Call<CommandResult>(note, "StageRename", new ModelWriteFilterPatch(1, "staged"))).Status.Should().Be(CommandStatus.Staged);
        (await Call<CommandResult>(audit, "StageAudit", new ModelWriteFilterPatch(50, "shared"))).Status.Should().Be(CommandStatus.Staged);
        Writes.Rows(data, mapped)[0].Deleted.Should().BeFalse(); data.PendingCount.Should().Be(2);
        (await data.SaveChangesAsync()).Should().Be(2);
        Writes.Rows(data, mapped)[0].Should().Be(new ModelWriteFilterSeed(1, "staged", 1, true, false));
        (await Call<IReadOnlyList<ModelWriteFilterCard>>(note, "Cards")).Select(row => row.Id).Should().Equal(4);
        ((RonSijm.RepoGen.Testing.InMemoryStore<MemoryWriteAudit, int>)data.GetType().GetProperty("MemoryWriteAuditStore")!.GetValue(data)!).Find(50)!.Text.Should().Be("shared");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetBasedCountsFilterSavedRowsWithoutFlushingOrRefreshingPendingWrites(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped);
        await Call<CommandResult>(note, "StageRename", new ModelWriteFilterPatch(1, "pending"));
        (await Call<CommandResult>(note, "UpdateAll")).AffectedRows.Should().Be(2);
        Writes.Rows(data, mapped)[0].Title.Should().Be("bulk"); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); Writes.Rows(data, mapped)[0].Title.Should().Be("pending");
        (await Call<CommandResult>(note, "ImmediateHide", 4)).AffectedRows.Should().Be(1);
        (await Call<CommandResult>(note, "ExecuteDelete", new ModelWriteFilterPatch(4, ""))).Status.Should().Be(CommandStatus.NotMatched);
        (await Call<CommandResult>(note, "Update", new ModelWriteFilterPatch(2, "blocked"))).AffectedRows.Should().Be(0);
        (await Call<CommandResult>(note, "Update", new ModelWriteFilterPatch(1, "stale", 99))).Status.Should().Be(CommandStatus.NotMatched);
        (await Call<CommandResult>(note, "ExecuteDelete", new ModelWriteFilterPatch(1, "", 1))).AffectedRows.Should().Be(1);
        Writes.Rows(data, mapped).Select(row => row.Id).Should().Equal(2, 3, 4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchesPrepareAllVisibleTargetsAndReturnFreshDtosInInputOrder(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped);
        IReadOnlyList<ModelWriteFilterPatch> input = [new(4, "fourth"), new(1, "first")];
        var result = await Call<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>(note, "RenameMany", (object)input);
        result.Value!.Select(row => row.Id).Should().Equal(4, 1); result.Value.Should().OnlyContain(row => row.Version == 1);
        IReadOnlyList<ModelWriteFilterPatch> invalid = [new(1, "must-not-change", 1), new(2, "deleted")];
        (await Call<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>(note, "RenameMany", (object)invalid)).Status.Should().Be(CommandStatus.NotFound);
        Writes.Rows(data, mapped)[0].Title.Should().Be("first"); data.PendingCount.Should().Be(0);
        (await Call<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>(note, "DeleteMany", (object)invalid)).Status.Should().Be(CommandStatus.NotFound);
        Writes.Rows(data, mapped).Should().HaveCount(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InsertionsAreNotFilteredButFreshSavedResultsAre(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped);
        var created = await Call<CommandResult<ModelWriteFilterCard>>(note, "Create", new ModelWriteFilterPatch(10, "deleted insert", Deleted: true));
        created.Succeeded.Should().BeTrue(); created.Value!.Deleted.Should().BeTrue();
        Func<Task> createSaved = async () => await Call<CommandResult<ModelWriteFilterCard>>(note, "CreateSaved", new ModelWriteFilterPatch(11, "invisible", Deleted: true));
        await createSaved.Should().ThrowAsync<RepositorySavedResultException>();
        Writes.Rows(data, mapped).Should().Contain(row => row.Id == 11 && row.Deleted);
        Func<Task> hide = async () => await Call<CommandResult<ModelWriteFilterCard>>(note, "HideAndRead", 1);
        await hide.Should().ThrowAsync<RepositorySavedResultException>(); Writes.Rows(data, mapped)[0].Deleted.Should().BeTrue();
        (await Call<IReadOnlyList<ModelWriteFilterCard>>(note, "Cards")).Select(row => row.Id).Should().Equal(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedBatchFailureRetainsWholeSaveAndExplicitTransactionCanRollback(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped);
        await using var transaction = await data.BeginTransactionAsync();
        IReadOnlyList<ModelWriteFilterPatch> input = [new(10, "visible"), new(11, "deleted", Deleted: true)];
        Func<Task> execute = async () => await Call<CommandResult<IReadOnlyList<ModelWriteFilterCard>>>(note, "CreateMany", (object)input);
        await execute.Should().ThrowAsync<RepositorySavedResultException>();
        Writes.Rows(data, mapped).Should().HaveCount(6); data.PendingCount.Should().Be(0);
        await transaction.RollbackAsync(); Writes.Rows(data, mapped).Should().HaveCount(4);
        (await Call<CommandResult<ModelWriteFilterCard>>(note, "Rename", new ModelWriteFilterPatch(1, "after"))).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationPrecedesFilteredWritesAndPreservesFixtureData(bool mapped)
    {
        var (note, _, data) = Writes.Fixture(mapped); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var task = (Task<CommandResult<ModelWriteFilterCard>>)note.GetType().GetMethod("RenameAsync")!.Invoke(note, [new ModelWriteFilterPatch(1, "never"), cancellation.Token])!;
        Func<Task> execute = async () => await task; await execute.Should().ThrowAsync<OperationCanceledException>();
        Writes.Rows(data, mapped)[0].Title.Should().Be("one"); data.PendingCount.Should().Be(0);
    }
}
