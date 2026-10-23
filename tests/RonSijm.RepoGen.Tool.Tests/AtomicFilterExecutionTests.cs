using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFilterExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InsertsAndConflictProbesNeverTreatFilteredRowsAsMissing(bool mapped)
    {
        var (repository, data, _) = Fixture(mapped);
        var deleted = await Call<CommandResult<ModelWriteFilterCard>>(repository, "Ensure", new AtomicFilterInput(2, "not inserted"));
        deleted.Status.Should().Be(CommandStatus.AlreadyExists); deleted.HasValue.Should().BeFalse();
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Ensure", new AtomicFilterInput(3))).Status.Should().Be(CommandStatus.AlreadyExists);
        var inserted = await Call<CommandResult<ModelWriteFilterCard>>(repository, "Ensure", new AtomicFilterInput(10, "deleted insert", Deleted: true, Hidden: true));
        inserted.Status.Should().Be(CommandStatus.Inserted); inserted.Value!.Deleted.Should().BeTrue();
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "Cards")).Select(row => row.Id).Should().Equal(1, 4);
        (await Call<CommandResult>(repository, "EnsureCount", new AtomicFilterInput(10))).Status.Should().Be(CommandStatus.AlreadyExists);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(11, "upsert insert", Deleted: true))).Status.Should().Be(CommandStatus.Inserted);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdatesRequireModelVisibilityAndTokensButDoNotReapplyDesignerVisibilityToResults(bool mapped)
    {
        var (repository, _, _) = Fixture(mapped);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(2))).Status.Should().Be(CommandStatus.NotMatched);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "PutBypass", new AtomicFilterInput(2))).Status.Should().Be(CommandStatus.NotMatched);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(3))).Status.Should().Be(CommandStatus.NotMatched);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "PutBypass", new AtomicFilterInput(3, "bypassed"))).Status.Should().Be(CommandStatus.Updated);
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(1, "stale", 99))).Status.Should().Be(CommandStatus.NotMatched);
        var hidden = await Call<CommandResult<ModelWriteFilterCard>>(repository, "HidePut", new AtomicFilterInput(1, "hidden result"));
        hidden.Status.Should().Be(CommandStatus.Updated); hidden.Value!.Title.Should().Be("hidden result"); hidden.Value.Version.Should().Be(1);
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "Cards")).Select(row => row.Id).Should().Equal(4);
        (await Call<CommandResult>(repository, "PutCount", new AtomicFilterInput(4, "counted"))).AffectedRows.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilterChangingUpsertsFailTheirFreshResultReadAndPreserveRowsAndStaging(bool mapped)
    {
        var (repository, data, _) = Fixture(mapped);
        await Call<CommandResult>(repository, "StageRename", new AtomicFilterInput(4, "pending"));
        Func<Task> archive = async () => await Call<CommandResult<ModelWriteFilterCard>>(repository, "ArchivePut", new AtomicFilterInput(1, "must not survive"));
        await archive.Should().ThrowAsync<InvalidOperationException>().WithMessage("*filtered result read*");
        var original = (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "All")).Single(row => row.Id == 1);
        original.Should().Be(new ModelWriteFilterSeed(1, "one", 0, false, false)); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1);
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "All")).Single(row => row.Id == 4).Title.Should().Be("pending");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AtomicUpdatesRetainPendingVersionsAndCheckpointRollback(bool mapped)
    {
        var (repository, data, _) = Fixture(mapped); data.CaptureBaseline();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            await Call<CommandResult<ModelWriteFilterCard>>(repository, "Ensure", new AtomicFilterInput(10));
            await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(1, "changed"));
            await transaction.RollbackAsync();
        }
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "All")).Should().HaveCount(4);
        await Call<CommandResult>(repository, "StageRename", new AtomicFilterInput(1, "pending"));
        await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(1, "native")); data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.DiscardChanges(); data.RestoreBaseline();
        await Call<CommandResult>(repository, "StageCreate", new AtomicFilterInput(10, "pending insert"));
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Ensure", new AtomicFilterInput(10, "saved insert"))).Status.Should().Be(CommandStatus.Inserted);
        Func<Task> duplicate = () => data.SaveChangesAsync(); await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate key*");
        data.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task KeyedPartitionsKeepTheirConflictRowsAndPendingWorkSeparate()
    {
        using var context = ModelWriteFilterTestHarness.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(keyed: true).Build()));
        var (repository, first, fixture) = Fixture(assembly: assembly, key: "A");
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!; Seed(second, true);
        await Call<CommandResult>(repository, "StageRename", new AtomicFilterInput(1, "pending", Partition: "A"));
        (await Call<CommandResult<ModelWriteFilterCard>>(repository, "Put", new AtomicFilterInput(1, "second", Partition: "B"))).Status.Should().Be(CommandStatus.Updated);
        first.PendingCount.Should().Be(1); second.PendingCount.Should().Be(0);
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "Cards", new AtomicFilterInput(0, Partition: "A")))[0].Title.Should().Be("one");
        (await Call<IReadOnlyList<ModelWriteFilterSeed>>(repository, "Cards", new AtomicFilterInput(0, Partition: "B")))[0].Title.Should().Be("second");
    }
}
