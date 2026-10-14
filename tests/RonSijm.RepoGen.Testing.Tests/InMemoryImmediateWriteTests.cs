using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryImmediateWriteTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data, string name = "rows")
    {
        var store = data.CreateStore<InMemoryWriteRow, int>(name, row => row.Id, InMemoryWriteRow.Copy);
        store.SeedRange([new() { Id = 1, Name = "one", Version = 1 }, new() { Id = 2, Name = "two", Version = 2 }]);
        return store;
    }
    private static Task<CommandResult<string>> Stage(InMemoryWriteDataContext data, InMemoryWriteStore<InMemoryWriteRow, int> store, int id = 1) =>
        data.PatchAsync(store, row => row.Id == id, row => { row.Name = "staged"; return [nameof(row.Name)]; }, _ => true,
            (left, right) => left.Name == right.Name && left.Version == right.Version, (left, right) => left.Version == right.Version,
            (target, source, assigned) => target.Name = source.Name, row => row.Name, false);

    [Fact]
    public async Task ReturningUpdatesReadOriginalScalarsAndExposeDetachedSavedRows()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        var result = await data.ExecuteUpdateReturningAsync(store, row => row.Id < 3,
            (original, candidate) => { candidate.Version = original.Version + 10; candidate.Unassigned = original.Version.ToString(System.Globalization.CultureInfo.InvariantCulture); }, row => row);
        result.AffectedRows.Should().Be(2);
        result.Items.Select(row => row.Unassigned).Should().Equal("1", "2");
        result.Items[0].Name = "caller change"; store.Find(1)!.Name.Should().Be("one");
        data.PendingCount.Should().Be(0); (await data.SaveChangesAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingRowsCountEvenWhenUnchangedAndEmptyPredicatesDoNotWrite(bool matches)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        (await data.ExecuteUpdateAsync(store, _ => matches, (original, candidate) => candidate.Name = original.Name)).Should().Be(matches ? 2 : 0);
        store.Count.Should().Be(2); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ImmediateWritesDoNotFlushRefreshOrLosePendingAssignments()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var other = Store(data, "other");
        await Stage(data, store); await Stage(data, other);
        await data.ExecuteUpdateAsync(store, row => row.Id == 1, (_, candidate) => candidate.Unassigned = "immediate");
        store.Find(1)!.Name.Should().Be("one"); data.PendingCount.Should().Be(2);
        (await data.SaveChangesAsync()).Should().Be(2);
        store.Find(1)!.Name.Should().Be("staged"); store.Find(1)!.Unassigned.Should().Be("immediate");
        other.Find(1)!.Name.Should().Be("staged");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BypassingTheTrackerCanLeavePendingTokenOrMissingRowConflicts(bool delete)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); await Stage(data, store);
        if (delete) await data.ExecuteDeleteAsync(store, row => row.Id == 1);
        else await data.ExecuteUpdateAsync(store, row => row.Id == 1, (original, candidate) => candidate.Version = original.Version + 1);
        data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.PendingCount.Should().Be(1); data.DiscardChanges();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturningMappingFailureNeverCommitsAnyTargetOrPendingChange(bool delete)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); await Stage(data, store);
        int Project(InMemoryWriteRow row) => row.Id == 2 ? throw new InvalidOperationException("mapping failed") : row.Version;
        Func<Task> execute = () => delete ? data.ExecuteDeleteReturningAsync(store, _ => true, Project)
            : data.ExecuteUpdateReturningAsync(store, _ => true, (_, candidate) => candidate.Version = 9, Project);
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("mapping failed");
        store.Snapshot().Select(row => row.Version).Should().Equal(1, 2); data.PendingCount.Should().Be(1);
    }

    [Theory]
    [InlineData("Setter")]
    [InlineData("GeneratedValue")]
    [InlineData("Key")]
    [InlineData("Reentry")]
    public async Task AllPreparationFailuresLeaveEveryRowUnchanged(string failure)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        if (failure == "GeneratedValue") data.ConfigureGeneratedValues(store, onUpdate: (row, _) =>
            { if (row.Id == 2) throw new InvalidOperationException("factory failed"); row.Version = 9; });
        Func<Task> execute = () => data.ExecuteUpdateAsync(store, _ => true, (_, candidate) =>
        {
            candidate.Name = "changed";
            if (candidate.Id != 2) return;
            if (failure == "Setter") throw new InvalidOperationException("setter failed");
            if (failure == "Key") candidate.Id = 3;
            if (failure == "Reentry") store.Find(1);
        });
        await execute.Should().ThrowAsync<InvalidOperationException>();
        store.Snapshot().Select(row => row.Name).Should().Equal("one", "two"); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitComputedFactoriesAlsoRunForImmediateUpdates()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        data.ConfigureGeneratedValues(store, onUpdate: (row, rows) => row.Version = rows.Max(value => value.Version) + 1);
        var result = await data.ExecuteUpdateReturningAsync(store, _ => true, (_, candidate) => candidate.Name = "updated", row => row.Version);
        result.Items.Should().Equal(3, 4); store.Snapshot().Select(row => row.Version).Should().Equal(3, 4);
    }

    [Theory]
    [InlineData("PreCancelled")]
    [InlineData("Selection")]
    [InlineData("Projection")]
    public async Task CancellationCannotPartlyConsumeRows(string timing)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); using var cancellation = new CancellationTokenSource();
        if (timing == "PreCancelled") cancellation.Cancel();
        Func<Task> execute = () => data.ExecuteDeleteReturningAsync(store,
            row => { if (timing == "Selection" && row.Id == 2) cancellation.Cancel(); return true; },
            row => { if (timing == "Projection" && row.Id == 2) cancellation.Cancel(); return row.Name; }, cancellation.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>(); store.Count.Should().Be(2); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckpointRollbackAndBaselineRestoreIncludeImmediateRowsAcrossStores()
    {
        var data = new InMemoryWriteDataContext(); var first = Store(data); var second = Store(data, "second"); data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await data.ExecuteDeleteAsync(first, _ => true);
            await data.ExecuteUpdateAsync(second, _ => true, (_, candidate) => candidate.Name = "updated");
            first.Count.Should().Be(0); second.Find(1)!.Name.Should().Be("updated");
        }
        first.Count.Should().Be(2); second.Find(1)!.Name.Should().Be("one");
        await data.ExecuteDeleteAsync(first, _ => true); data.RestoreBaseline(); first.Count.Should().Be(2);
        await using var transaction = await data.BeginTransactionAsync(); await data.ExecuteDeleteAsync(first, _ => true); await transaction.CommitAsync();
        first.Count.Should().Be(0);
    }

    [Fact]
    public async Task PublicWriteEncoderFailureCannotPartlyUpdateAndDirectWritesCannotBypassIt()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryMappedWriteData, int>("mapped", row => row.Key, row => new(row.Key, row.Caption, row.Revision, row.Extra));
        var writes = data.MapWriteStore(store, row => new InMemoryWriteRow { Id = row.Key, Name = row.Caption, Version = row.Revision },
            (row, saved) => row.Id == 2 ? throw new InvalidOperationException("encode failed") : new InMemoryMappedWriteData(row.Id, row.Name, row.Version, saved!.Extra),
            InMemoryWriteRow.Copy, row => row.Id, (left, right) => left.Id == right.Id && left.Name == right.Name && left.Version == right.Version);
        store.SeedRange([new(1, "one", 1, "kept"), new(2, "two", 2, "also kept")]);
        Func<Task> execute = () => data.ExecuteUpdateAsync(writes, _ => true, (_, candidate) => candidate.Name = "changed");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("encode failed");
        store.Snapshot().Select(row => row.Caption).Should().Equal("one", "two");
        Func<Task> bypass = () => data.ExecuteDeleteAsync(store, _ => true); await bypass.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mapped write view*");
        (await data.ExecuteDeleteReturningAsync(writes, _ => true, row => row.Name)).Items.Should().Equal("one", "two"); store.Count.Should().Be(0);
    }

    [Fact]
    public async Task ForeignAndDisposedContextsCannotBeWritten()
    {
        var data = new InMemoryWriteDataContext(); var foreign = Store(new());
        Func<Task> wrong = () => data.ExecuteDeleteAsync(foreign, _ => true); await wrong.Should().ThrowAsync<ArgumentException>();
        var store = Store(data); await data.DisposeAsync();
        Func<Task> disposed = () => data.ExecuteDeleteAsync(store, _ => true); await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }
}
