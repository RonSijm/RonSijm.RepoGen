using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateReturnsPostMutationValuesEvenWhenTheyStopMatchingTheModelFilter(bool mapped)
    {
        var (repository, _) = Fixture(mapped);
        var changed = await Call<MutationResult<ModelWriteFilterCard>>(repository, "ArchiveAll");
        changed.AffectedRows.Should().Be(2); changed.Items.OrderBy(row => row.Id).Should().Equal(new ModelWriteFilterCard(1, "one", 1, true), new ModelWriteFilterCard(4, "four", 1, true));
        (await Call<IReadOnlyList<ModelWriteFilterCard>>(repository, "Cards")).Should().BeEmpty();
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "All")).Should().HaveCount(4);
        (await Call<MutationResult<ModelWriteFilterCard>>(repository, "ArchiveAll")).AffectedRows.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteReturnsOnlyMatchedPreDeleteValuesAndBypassDoesNotBypassTheModelFilter(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        (await Call<MutationResult<ModelWriteFilterCard>>(repository, "Consume", new ModelWriteFilterPatch(2, ""))).Items.Should().BeEmpty();
        (await Call<MutationResult<ModelWriteFilterCard>>(repository, "ConsumeBypass", new ModelWriteFilterPatch(2, ""))).Items.Should().BeEmpty();
        (await Call<MutationResult<ModelWriteFilterCard>>(repository, "Consume", new ModelWriteFilterPatch(3, ""))).Items.Should().BeEmpty();
        (await Call<MutationResult<ModelWriteFilterCard>>(repository, "Consume", new ModelWriteFilterPatch(1, "", 99))).Items.Should().BeEmpty();
        var bypassed = await Call<MutationResult<ModelWriteFilterCard>>(repository, "ConsumeBypass", new ModelWriteFilterPatch(3, ""));
        bypassed.Items.Should().Equal(new ModelWriteFilterCard(3, "hidden", 0, false));
        var consumed = await Call<MutationResult<ModelWriteFilterCard>>(repository, "ConsumeAll");
        consumed.Items.Select(row => row.Id).Should().BeEquivalentTo([1, 4]);
        ModelWriteFilterTestHarness.Rows(data, mapped).Select(row => row.Id).Should().Equal(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeResultsDoNotFlushPendingChangesAndTokensStillDetectLostUpdates(bool mapped)
    {
        var (repository, data) = Fixture(mapped);
        await Call<CommandResult>(repository, "StageTitle", new ModelWriteFilterPatch(1, "pending"));
        var result = await Call<MutationResult<ModelWriteFilterCard>>(repository, "Update", new ModelWriteFilterPatch(1, "native"));
        result.Items.Should().Equal(new ModelWriteFilterCard(1, "native", 1, false)); data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.DiscardChanges();
        await Call<CommandResult>(repository, "StageTitle", new ModelWriteFilterPatch(1, "pending again", 1));
        var marked = await Call<MutationResult<ModelWriteFilterCard>>(repository, "Mark", 1);
        marked.Items.Single().Deleted.Should().BeTrue(); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1);
        ModelWriteFilterTestHarness.Rows(data, mapped)[0].Should().Be(new ModelWriteFilterSeed(1, "pending again", 1, true, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProjectionFailureCancellationAndCheckpointsPreserveExistingImmediateSemantics(bool mapped)
    {
        var (repository, data) = Fixture(mapped); data.CaptureBaseline();
        var before = ModelWriteFilterTestHarness.Rows(data, mapped);
        foreach (var name in new[] { "FailUpdateResult", "FailDeleteResult" })
        {
            Func<Task> fail = async () => await Call<MutationResult<NativeFilterThrowingCard>>(repository, name);
            await fail.Should().ThrowAsync<InvalidOperationException>();
            ModelWriteFilterTestHarness.Rows(data, mapped).Should().Equal(before); data.PendingCount.Should().Be(0);
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Func<Task> cancelled = async () => await (Task<MutationResult<ModelWriteFilterCard>>)repository.GetType().GetMethod("ArchiveAllAsync")!.Invoke(repository, [cancellation.Token])!;
        await cancelled.Should().ThrowAsync<OperationCanceledException>(); ModelWriteFilterTestHarness.Rows(data, mapped).Should().Equal(before);
        await using (var transaction = await data.BeginTransactionAsync())
        {
            (await Call<MutationResult<ModelWriteFilterCard>>(repository, "ConsumeAll")).AffectedRows.Should().Be(2);
            await transaction.RollbackAsync();
        }
        ModelWriteFilterTestHarness.Rows(data, mapped).Should().Equal(before);
        await Call<MutationResult<ModelWriteFilterCard>>(repository, "ArchiveAll"); data.RestoreBaseline();
        ModelWriteFilterTestHarness.Rows(data, mapped).Should().Equal(before);
    }
}
