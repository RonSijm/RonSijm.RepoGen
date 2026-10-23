using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnExecutionTests
{
    [Fact]
    public async Task UpdateReturnsFreshComputedShadowAndGetterFreeFieldIndexerValues()
    {
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Assembly);
        MappedStateNote.GetterCalls.Value = 0;
        var result = await Call<MutationResult<MappedStateCard>>(repository, "RenameAllReturnedAsync");
        result.AffectedRows.Should().Be(2);
        result.Items.OrderBy(item => item.Id).Select(item => item.Revision).Should().Equal(4, 6);
        result.Items.Should().OnlyContain(item => item.Title == "returned" && item.Caption == "caption" && item.Audit == 10);
        result.Items.Single(item => item.Id == 1).Stamp.Should().Equal(1, 2);
        result.Items.Single(item => item.Id == 2).Stamp.Should().BeNull();
        result.Items.Single(item => item.Id == 2).OptionalVersion.Should().BeNull();
        store.Find(1)!.Revision.Should().Be(4);
        store.Find(3)!.Title.Should().Be("hidden");
        data.PendingCount.Should().Be(0);
        MappedStateNote.GetterCalls.Value.Should().Be(0);
        result.Items.Single(item => item.Id == 1).Stamp![0] = 99;
        store.Find(1)!.Stamp.Should().Equal(1, 2);
    }

    [Fact]
    public async Task DeleteChecksAllTypedTokensAndReturnsOriginalStateWithoutRunningFactories()
    {
        var (repository, _, store) = Fixture(MappedReturnTestHarness.Assembly);
        var wrong = await Call<MutationResult<MappedStateCard>>(repository, "ConsumeReturnedAsync", new MappedStateRequest(1, "unused", Stamp: [9]));
        wrong.AffectedRows.Should().Be(0);
        var first = await Call<MutationResult<MappedStateCard>>(repository, "ConsumeReturnedAsync", new MappedStateRequest(1, "unused", Stamp: [1, 2]));
        first.Items.Single().Revision.Should().Be(3);
        first.Items.Single().Counter.Should().Be(2);
        store.Find(1).Should().BeNull();
        var second = await Call<MutationResult<MappedStateCard>>(repository, "ConsumeReturnedAsync", new MappedStateRequest(2, "unused", 5, null, null));
        second.Items.Single().Revision.Should().Be(5);
        second.Items.Single().OptionalVersion.Should().BeNull();
        store.Find(2).Should().BeNull();
        store.Find(3).Should().NotBeNull();
    }

    [Theory]
    [InlineData("RenameReturned", 1, 99)]
    [InlineData("RenameReturned", 3, 3)]
    [InlineData("RenameReturned", 99, 3)]
    public async Task NoMatchNeverPretendsToDistinguishStaleHiddenAndMissing(string operation, int id, int version)
    {
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Assembly);
        var before = JsonSerializer.Serialize(store.Snapshot());
        (await Call<MutationResult<MappedStateCard>>(repository, operation + "Async", new MappedStateRequest(id, "unused", version))).Items.Should().BeEmpty();
        JsonSerializer.Serialize(store.Snapshot()).Should().Be(before);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData("FailUpdateReturned")]
    [InlineData("FailDeleteReturned")]
    public async Task MultirowResultFailureDoesNotPartlyCommitOrFlushPendingWork(string operation)
    {
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Assembly);
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "pending"));
        var before = JsonSerializer.Serialize(store.Snapshot());
        Func<Task> execute = () => Call<MutationResult<MappedReturnFailure>>(repository, operation + "Async");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("Returned-value mapping failed*");
        JsonSerializer.Serialize(store.Snapshot()).Should().Be(before);
        data.PendingCount.Should().Be(1);
        await data.SaveChangesAsync();
        store.Find(1)!.Title.Should().Be("pending");
        store.Find(1)!.Revision.Should().Be(4);
    }

    [Fact]
    public async Task ImmediateResultsPreservePendingOriginalVersionsAndCheckpointBaselines()
    {
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Assembly);
        data.CaptureBaseline();
        await using (var checkpoint = await data.BeginTransactionAsync())
        {
            await Call<MutationResult<MappedStateCard>>(repository, "RenameReturnedAsync", new MappedStateRequest(1, "unused"));
            store.Find(1)!.Revision.Should().Be(4);
        }
        store.Find(1)!.Revision.Should().Be(3);
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "pending"));
        await Call<MutationResult<MappedStateCard>>(repository, "RenameReturnedAsync", new MappedStateRequest(1, "unused"));
        data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().Be(1);
        data.DiscardChanges();
        data.RestoreBaseline();
        store.Find(1)!.Revision.Should().Be(3);
    }

    [Fact]
    public async Task PreCanceledReturnedWriteCannotAlterSavedOrPendingState()
    {
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Assembly);
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "pending"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var method = repository.GetType().GetMethod("ConsumeAllReturnedAsync")!;
        Func<Task> execute = async () => await (Task<MutationResult<MappedStateCard>>)method.Invoke(repository, [cancellation.Token])!;
        await execute.Should().ThrowAsync<OperationCanceledException>();
        store.Find(1)!.Revision.Should().Be(3);
        data.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task LossyEncoderRejectsEveryPreparedUpdateBeforeAnyTargetCommits()
    {
        var builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().MapInMemoryWrites<MappedStateSeed>((row, saved) => new(row.Id, row.Title, row.Counter,
            row.Caption, Microsoft.EntityFrameworkCore.EF.Property<int>(row, "audit"),
            Microsoft.EntityFrameworkCore.EF.Property<int>(row, "Revision") + (row.Title == "returned" ? 1 : 0),
            Microsoft.EntityFrameworkCore.EF.Property<int?>(row, "OptionalVersion"), (byte[]?)row["Stamp"], saved == null ? "created" : saved.Extra),
            useMappedPropertyState: true);
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Compile(builder));
        var before = JsonSerializer.Serialize(store.Snapshot());
        Func<Task> execute = () => Call<MutationResult<MappedStateCard>>(repository, "RenameAllReturnedAsync");
        await execute.Should().ThrowAsync<InvalidOperationException>();
        JsonSerializer.Serialize(store.Snapshot()).Should().Be(before);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitFactoryFailureCannotPartlyPublishReturnedRows()
    {
        var builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().WithoutInMemoryGeneratedValue("Revision")
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => checked(row.Counter + 1));
        var (repository, data, store) = Fixture(MappedReturnTestHarness.Compile(builder));
        store.Seed(Seeds()[1] with { Counter = int.MaxValue - 1, Revision = int.MaxValue }, InMemoryDuplicateKeyBehavior.Replace);
        var before = JsonSerializer.Serialize(store.Snapshot());
        Func<Task> execute = () => Call<MutationResult<MappedStateCard>>(repository, "RenameAllReturnedAsync");
        await execute.Should().ThrowAsync<OverflowException>();
        JsonSerializer.Serialize(store.Snapshot()).Should().Be(before);
        data.PendingCount.Should().Be(0);
    }
}
