using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateExecutionTests
{
    [Fact]
    public async Task StagingPreservesSavedTokensAndSharedSavePublishesMappedStateWithoutGetters()
    {
        var before = MappedStateNote.GetterCalls.Value;
        var (repository, data, store) = Fixture();
        (await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "staged"))).Status.Should().Be(CommandStatus.Staged);
        store.Find(1)!.Revision.Should().Be(3);
        data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1);
        store.Find(1)!.Should().BeEquivalentTo(Seeds()[0] with { Title = "staged", Counter = 3, Revision = 4 });
        MappedStateNote.GetterCalls.Value.Should().Be(before);
    }

    [Fact]
    public async Task SavedResultsAreFreshAndStaleMissingHiddenAndBinaryTokensKeepTheirOutcomes()
    {
        var (repository, data, store) = Fixture();
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "stale", 99))).Status.Should().Be(CommandStatus.Conflict);
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(99, "missing"))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(3, "hidden"))).Status.Should().Be(CommandStatus.NotFound);
        (await Call<CommandResult>(repository, "AllTokensAsync", new MappedStateRequest(1, "bad", Stamp: [9]))).Status.Should().Be(CommandStatus.Conflict);
        (await Call<CommandResult>(repository, "AllTokensAsync", new MappedStateRequest(1, "matched", Stamp: [1, 2]))).Status.Should().Be(CommandStatus.Applied);
        var result = await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "fresh"));
        result.Value!.Revision.Should().Be(4);
        result.Value.Caption.Should().Be("caption");
        result.Value.Stamp![0] = 99;
        store.Find(1)!.Stamp.Should().Equal(1, 2);
        data.PendingCount.Should().Be(0);
        (await Call<CommandResult>(repository, "AllTokensAsync", new MappedStateRequest(2, "nullable", 5, null))).Status.Should().Be(CommandStatus.Applied);
    }

    [Fact]
    public async Task BatchValidationIsAtomicAndSavedResultsRetainInputOrder()
    {
        var (repository, data, store) = Fixture();
        IReadOnlyList<MappedStateRequest> stale = [new(2, "changed", 5), new(1, "stale", 99)];
        (await Call<CommandResult<IReadOnlyList<MappedStateCard>>>(repository, "RenameManyAsync", stale)).Status.Should().Be(CommandStatus.Conflict);
        store.Find(2)!.Title.Should().Be("other");
        data.PendingCount.Should().Be(0);
        IReadOnlyList<MappedStateRequest> input = [new(2, "two", 5), new(1, "one")];
        var result = await Call<CommandResult<IReadOnlyList<MappedStateCard>>>(repository, "RenameManyAsync", input);
        result.Value!.Select(row => row.Id).Should().Equal(2, 1);
        result.Value!.Select(row => row.Revision).Should().Equal(6, 4);
        (await Call<CommandResult>(repository, "RemoveManyAsync", (IReadOnlyList<MappedStateRequest>)[new(2, "", 6), new(1, "", 4)]))
            .Status.Should().Be(CommandStatus.Applied);
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task NamedFactoriesSupplyCreateValuesAndRollbackRestoresMappedTokens()
    {
        var (repository, data, store) = Fixture();
        data.CaptureBaseline();
        var created = await Call<CommandResult<MappedStateCard>>(repository, "CreateAsync", new MappedStateRequest(10, "created"));
        created.Value!.Should().BeEquivalentTo(new MappedStateCard(10, "created", 0, "caption", 10, 1, 1, [1, 2]));
        store.Find(10)!.Extra.Should().Be("created");
        await using (var transaction = await data.BeginTransactionAsync())
        {
            await Call<CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "rolled"));
            await transaction.RollbackAsync();
        }
        store.Find(1)!.Revision.Should().Be(3);
        await Call<CommandResult>(repository, "StageRemoveAsync", new MappedStateRequest(1, ""));
        store.Find(1).Should().NotBeNull();
        await data.SaveChangesAsync();
        store.Find(1).Should().BeNull();
        data.RestoreBaseline();
        store.Find(1)!.Revision.Should().Be(3);
        store.Find(10).Should().BeNull();
    }

    [Fact]
    public async Task ImmediateWritesUseSavedMappedTokensWithoutFlushingPendingWrites()
    {
        var (repository, data, store) = Fixture();
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(2, "pending", 5));
        (await Call<int>(repository, "ExecuteRenameAsync", new MappedStateRequest(1, "stale", 99))).Should().Be(0);
        (await Call<int>(repository, "ExecuteRenameAsync", new MappedStateRequest(1, "immediate"))).Should().Be(1);
        store.Find(1)!.Revision.Should().Be(4);
        store.Find(2)!.Title.Should().Be("other");
        data.PendingCount.Should().Be(1);
        await data.SaveChangesAsync();
        store.Find(2)!.Revision.Should().Be(6);
    }
}
