using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedAtomicTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicExecutionTests
{
    [Fact]
    public async Task InsertAndExistingResultsUseExactGeneratedSlotsWithoutEntityGetters()
    {
        var (repository, _, store) = Fixture();
        MappedStateNote.GetterCalls.Value = 0;
        var input = new MappedAtomicRequest(20, "new ' quoted", 4);
        var inserted = await Call<CommandResult<MappedStateCard>>(repository, "Ensure", input);
        inserted.Status.Should().Be(CommandStatus.Inserted);
        inserted.Value.Should().BeEquivalentTo(new MappedStateCard(20, input.Title, 4, "NEW ' QUOTED", 14, 5, 1, [1, 2]));
        inserted.Value!.Stamp![0] = 99;
        store.Find(20)!.Stamp.Should().Equal(1, 2);
        var existing = await Call<CommandResult<MappedStateCard>>(repository, "Ensure", input with { Id = 900 });
        existing.Status.Should().Be(CommandStatus.AlreadyExists);
        existing.HasValue.Should().BeFalse();
        store.Find(900).Should().BeNull();
        MappedStateNote.GetterCalls.Value.Should().Be(0);
    }

    [Fact]
    public async Task UpsertRetainsSavedIdentityAndUsesStructuralOriginalTokensAndUpdatedFactories()
    {
        var (repository, _, store) = Fixture();
        MappedStateNote.GetterCalls.Value = 0;
        var result = await Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, "one", 2, Stamp: [1, 2]));
        result.Status.Should().Be(CommandStatus.Updated);
        result.Value.Should().BeEquivalentTo(new MappedStateCard(1, "one", 4, "ONE", 14, 5, 1, [1, 2]));
        store.Find(900).Should().BeNull();
        store.Find(1)!.Extra.Should().Be("arranged");
        MappedStateNote.GetterCalls.Value.Should().Be(0);
    }

    [Theory]
    [InlineData("one", 99, 1, false)]
    [InlineData("one", 3, null, false)]
    [InlineData("one", 3, 1, true)]
    [InlineData("hidden", 3, 1, false)]
    [InlineData("filtered", 11, 1, false)]
    public async Task FailedOriginalOrVisibilityConditionsNeverReturnHiddenState(string title, int revision, int? optional, bool wrongStamp)
    {
        var (repository, _, store) = Fixture();
        var before = store.Snapshot();
        var result = await Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, title, Revision: revision,
            OptionalVersion: optional, Stamp: wrongStamp ? [9] : [1, 2]));
        result.Status.Should().Be(CommandStatus.NotMatched);
        result.HasValue.Should().BeFalse();
        store.Snapshot().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("filtered")]
    public async Task ConflictProbesRemainUnfiltered(string title)
    {
        var (repository, _, store) = Fixture();
        var result = await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(900, title));
        result.Status.Should().Be(CommandStatus.AlreadyExists);
        result.HasValue.Should().BeFalse();
        store.Find(900).Should().BeNull();
    }

    [Fact]
    public async Task NullOriginalVersionsAndUnfilteredInsertionsRemainIndependentChoices()
    {
        var (repository, _, _) = Fixture();
        var updated = await Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, "other", Revision: 5, OptionalVersion: null));
        updated.Status.Should().Be(CommandStatus.Updated);
        updated.Value!.OptionalVersion.Should().BeNull();
        updated.Value.Stamp.Should().BeNull();
        var inserted = await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "outside filter", 10));
        inserted.Status.Should().Be(CommandStatus.Inserted);
        inserted.Value!.Revision.Should().Be(11);
    }

    [Fact]
    public async Task NativeCaptureRetainsStagingAndSharedSaveStillChecksOriginalVersions()
    {
        var (repository, data, store) = Fixture();
        await Call<CommandResult>(repository, "StageCounter", new(2, "ignored", Revision: 5));
        await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"));
        store.Find(2)!.Counter.Should().Be(4);
        await data.SaveChangesAsync();
        store.Find(2)!.Revision.Should().Be(6);
        await Call<CommandResult>(repository, "StageCounter", new(1, "ignored"));
        await Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, "one", Stamp: [1, 2]));
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        store.Find(1)!.Revision.Should().Be(4);
        data.PendingCount.Should().Be(1);
    }

    [Theory]
    [InlineData("FailEnsure", "new")]
    [InlineData("FailPut", "one")]
    public async Task ResultFailurePreservesSavedRowsAndPriorUnrelatedStaging(string operation, string title)
    {
        var (repository, data, store) = Fixture();
        await Call<CommandResult>(repository, "StageCounter", new(2, "ignored", Revision: 5));
        var before = store.Snapshot();
        Func<Task> fail = () => Call<CommandResult<MappedAtomicFailure>>(repository, operation, new(20, title, Stamp: [1, 2]));
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("atomic mapped result failed:*");
        store.Snapshot().Should().BeEquivalentTo(before);
        data.PendingCount.Should().Be(1);
        await data.SaveChangesAsync();
        store.Find(2)!.Revision.Should().Be(6);
    }

    [Fact]
    public async Task UpdatedFilteredResultFailureAndCancellationCannotPublishPartialState()
    {
        var (repository, _, store) = Fixture();
        var before = store.Snapshot();
        Func<Task> filtered = () => Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, "one", 7, Stamp: [1, 2]));
        await filtered.Should().ThrowAsync<InvalidOperationException>().WithMessage("*filtered result read*");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> canceled = () => Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"), cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        store.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task CheckpointsAndBaselinesRestoreCapturedScalarResults()
    {
        var (repository, data, store) = Fixture();
        data.CaptureBaseline();
        await using (var checkpoint = await data.BeginTransactionAsync())
        {
            await Call<CommandResult<MappedStateCard>>(repository, "Put", new(900, "one", Stamp: [1, 2]));
            await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"));
            await checkpoint.RollbackAsync();
        }
        store.Find(1)!.Revision.Should().Be(3);
        store.Find(20).Should().BeNull();
        await Call<CommandResult<MappedStateCard>>(repository, "Ensure", new(20, "new"));
        data.RestoreBaseline();
        store.Find(20).Should().BeNull();
    }
}
