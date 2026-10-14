using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.BatchCommandTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class BatchCommandExecutionTests
{
    [Fact]
    public async Task SavedPatchesUseOneSharedSavePreserveFieldsAndReturnInputOrderedVersions()
    {
        await using var fixture = await CreateAsync();
        var operations = Operations(Model(fixture.Context), fixture.Context);
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", Request(new("B", "Renamed B"), new("A", "Renamed A")));
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Select(row => row.Code).Should().Equal("B", "A");
        result.Value.Should().OnlyContain(row => row.Version == 2);
        fixture.Context.Saves.Should().Be(1);
        (await fixture.Context.Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Title).ToArrayAsync()).Should().Equal("Renamed A", "Renamed B", "Other owner");
        (await fixture.Context.Rows.AsNoTracking().Select(row => row.Origin).Distinct().ToListAsync()).Should().Equal("Keep");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("otherOwner")]
    [InlineData("globalFilter")]
    [InlineData("stale")]
    public async Task AFailingLaterItemLeavesTheWholeBatchUnchanged(string scenario)
    {
        await using var fixture = await CreateAsync();
        // Include a hidden tracked row: SQL visibility, not tracker membership, selects targets.
        await fixture.Context.Rows.IgnoreQueryFilters().SingleAsync(row => row.Code == "H");
        var item = scenario switch
        {
            "missing" => new BatchItem("Unknown", "No"),
            "otherOwner" => new BatchItem("Z", "No"),
            "globalFilter" => new BatchItem("H", "No"),
            _ => new BatchItem("B", "No", ExpectedVersion: 0)
        };
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(fixture.Context), fixture.Context), "Rename", Request(new("A", "Must restore"), item));
        result.Status.Should().Be(scenario == "stale" ? CommandStatus.Conflict : CommandStatus.NotFound);
        fixture.Context.Saves.Should().Be(0);
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await fixture.Context.Rows.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Title).ToListAsync()).Should().Equal("First", "Second", "Other owner", "Hidden");
    }

    [Fact]
    public async Task StagingDoesNotPersistAndOneLaterSaveAlsoPersistsUnrelatedChanges()
    {
        await using var fixture = await CreateAsync();
        var other = await fixture.Context.Rows.SingleAsync(row => row.Code == "Z");
        other.Title = "Shared pending";
        var result = await Invoke<CommandResult>(Operations(Model(fixture.Context), fixture.Context), "Stage", Request(new("A", "Staged A"), new("B", "Staged B")));
        result.Status.Should().Be(CommandStatus.Staged);
        fixture.Context.Saves.Should().Be(0);
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
        (await fixture.Context.SaveChangesAsync()).Should().Be(3);
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Code == "Z")).Title.Should().Be("Shared pending");
    }

    [Fact]
    public async Task TrackedDeleteReturnsOnlyRequestedRowsAndChecksEveryOriginalVersion()
    {
        await using var fixture = await CreateAsync();
        var operations = Operations(Model(fixture.Context), fixture.Context);
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Delete", Request(new("A", ""), new("B", "", 0)))).Status.Should().Be(CommandStatus.Conflict);
        (await fixture.Context.Rows.CountAsync()).Should().Be(3);
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Delete", Request(new("B", ""), new("A", "")));
        result.Value!.Select(row => row.Code).Should().Equal("B", "A");
        fixture.Context.Saves.Should().Be(1);
        (await fixture.Context.Rows.Select(row => row.Code).ToListAsync()).Should().Equal("Z");
    }

    [Fact]
    public async Task ConditionalOmissionWithoutVersionSettersDoesNotSave()
    {
        await using var fixture = await CreateAsync();
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(fixture.Context, versions: false), fixture.Context), "Rename",
            Request(new("A", "Ignored", Supplied: false), new("B", "Ignored", Supplied: false)));
        result.Status.Should().Be(CommandStatus.NoChanges);
        fixture.Context.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("nullList")]
    [InlineData("nullItem")]
    [InlineData("nullKey")]
    [InlineData("duplicate")]
    [InlineData("tooMany")]
    [InlineData("cancelled")]
    [InlineData("empty")]
    public async Task InputChecksAndEmptyBatchesHappenBeforeOwnedAcquisition(string scenario)
    {
        await using var fixture = await CreateAsync();
        var provider = new OwnedProvider(fixture.Connection);
        var operations = Operations(Model(fixture.Context, leases: true, maximum: 2), provider);
        var request = scenario switch
        {
            "nullList" => new BatchRequest(1, null!),
            "nullItem" => Request((BatchItem)null!),
            "nullKey" => Request(new BatchItem(null!, "Invalid")),
            "duplicate" => Request(new("A", "One"), new("A", "Two")),
            "tooMany" => Request(new("A", "One"), new("B", "Two"), new("Z", "Three")),
            _ => Request()
        };
        if (scenario == "empty")
        {
            (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", request)).Status.Should().Be(CommandStatus.NoChanges);
        }
        else
        {
            Func<Task> invalid = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", request,
                scenario == "cancelled" ? new CancellationToken(true) : CancellationToken.None);
            if (scenario == "cancelled") await invalid.Should().ThrowAsync<OperationCanceledException>();
            else await invalid.Should().ThrowAsync<ArgumentException>();
        }

        provider.Contexts.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOwnedLeaseIsDisposedAndCannotStagePendingWrites()
    {
        await using var fixture = await CreateAsync();
        var provider = new OwnedProvider(fixture.Connection);
        var operations = Operations(Model(fixture.Context, leases: true), provider);
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", Request(new BatchItem("A", "Owned")))).Succeeded.Should().BeTrue();
        provider.Contexts.Single().Disposals.Should().Be(1);
        Func<Task> invalid = async () => await Invoke<CommandResult>(operations, "Stage", Request(new BatchItem("B", "Cannot stage")));
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*staged command*owned context*");
        provider.Contexts.Last().Disposals.Should().Be(1);
    }

    [Fact]
    public async Task PendingTargetChangesAreRejectedWithoutOverwritingThem()
    {
        await using var fixture = await CreateAsync();
        var row = await fixture.Context.Rows.SingleAsync(row => row.Code == "B");
        row.Title = "Keep pending";
        Func<Task> invalid = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(fixture.Context), fixture.Context), "Rename", Request(new("A", "No"), new("B", "No")));
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*");
        row.Title.Should().Be("Keep pending");
        fixture.Context.Entry(row).State.Should().Be(EntityState.Modified);
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
    }

    [Fact]
    public async Task ADatabaseRaceRestoresAllTargetScalarsAndPreservesOtherPendingEdits()
    {
        await using var fixture = await CreateAsync();
        await using var context = CreateContext(fixture.Connection, new VersionRace());
        var other = await context.Rows.SingleAsync(row => row.Code == "Z");
        other.Title = "Unrelated pending";
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(context), context), "Rename", Request(new("A", "Lost A"), new("B", "Lost B")));
        result.Status.Should().Be(CommandStatus.Conflict);
        context.ChangeTracker.Entries<BatchRow>().Where(entry => entry.Entity.Code != "Z").Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        other.Title.Should().Be("Unrelated pending");
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Code == "B")).Version.Should().Be(2);
    }

    [Fact]
    public async Task SharedTransactionsCanRollBackASuccessfulBatch()
    {
        await using var fixture = await CreateAsync();
        await using var work = new RepositoryUnitOfWork<BatchContext>(fixture.Context);
        await using var transaction = await work.BeginTransactionAsync();
        var result = await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(fixture.Context, leases: true), work), "Rename", Request(new("A", "In transaction"), new("B", "In transaction")));
        result.Succeeded.Should().BeTrue();
        await transaction.RollbackAsync();
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
    }

    private static BatchRequest Request(params BatchItem[] items) => new(1, items);
    private sealed class OwnedProvider(SqliteConnection connection) : IDbContextLeaseProvider<BatchContext>
    {
        internal List<BatchContext> Contexts { get; } = [];
        public ValueTask<DbContextLease<BatchContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            var context = CreateContext(connection);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context, "batch"));
        }
    }

    private sealed class VersionRace : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await eventData.Context!.Database.ExecuteSqlRawAsync("UPDATE Rows SET Version = Version + 1 WHERE Code = 'B'", cancellationToken);
            return result;
        }
    }
}
