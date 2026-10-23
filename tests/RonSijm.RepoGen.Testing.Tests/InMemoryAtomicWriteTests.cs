using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryAtomicWriteTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data)
    {
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        store.Seed(new() { Id = 1, Name = "one", Version = 1 });
        return store;
    }
    private static bool Conflict(InMemoryWriteRow left, InMemoryWriteRow right) => left.Name == right.Name;
    private static InMemoryWriteRow Input(string name = "new", int id = 2) => new() { Id = id, Name = name, Version = 1 };
    private static Task<CommandResult<InMemoryWriteRow>> Insert(InMemoryWriteDataContext data, InMemoryWriteStore<InMemoryWriteRow, int> store,
        string name = "new", int id = 2, CancellationToken cancellationToken = default)
        => data.ExecuteInsertIfAbsentAsync(store, () => Input(name, id), Conflict, row => row, cancellationToken);
    private static Task<CommandResult<InMemoryWriteRow>> Upsert(InMemoryWriteDataContext data, InMemoryWriteStore<InMemoryWriteRow, int> store,
        string name = "one", bool matches = true)
        => data.ExecuteInsertOrUpdateAsync(store, () => Input(name), Conflict, _ => matches,
            (original, candidate) => { candidate.Version = original.Version + 1; candidate.Unassigned = original.Version.ToString(System.Globalization.CultureInfo.InvariantCulture); }, row => row);

    [Fact]
    public async Task InsertAndExistingOutcomesReturnOnlyDetachedInsertedValues()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        var input = Input();
        var result = await data.ExecuteInsertIfAbsentAsync(store, () => input, Conflict, row => row);
        result.Status.Should().Be(CommandStatus.Inserted); result.AffectedRows.Should().Be(1); result.HasValue.Should().BeTrue();
        input.Version = 99; result.Value!.Version = 88; store.Find(2)!.Version.Should().Be(1);
        var duplicate = await Insert(data, store, "new", 99);
        duplicate.Status.Should().Be(CommandStatus.AlreadyExists); duplicate.AffectedRows.Should().Be(0); duplicate.HasValue.Should().BeFalse();
        store.Count.Should().Be(2); data.PendingCount.Should().Be(0); (await data.SaveChangesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConditionalUpsertUsesOriginalRowAndPreservesItsKeyRatherThanTheInputKey()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        var updated = await Upsert(data, store);
        updated.Status.Should().Be(CommandStatus.Updated); updated.Value!.Id.Should().Be(1);
        updated.Value.Version.Should().Be(2); updated.Value.Unassigned.Should().Be("1");
        var notMatched = await Upsert(data, store, matches: false);
        notMatched.Status.Should().Be(CommandStatus.NotMatched); notMatched.HasValue.Should().BeFalse();
        var inserted = await Upsert(data, store, "new", matches: false);
        inserted.Status.Should().Be(CommandStatus.Inserted); // Existing-row predicates never prevent insertion.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenUniqueTargetArrangementIsRejectedBeforeAnyWrite(bool upsert)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); store.Seed(Input("one", 2));
        Func<Task> action = () => upsert ? Upsert(data, store) : Insert(data, store, "one", 3);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Multiple saved rows*");
        store.Snapshot().Select(row => row.Version).Should().Equal(1, 1);
    }

    [Fact]
    public async Task DuplicatePrimaryKeysAreNotMisreportedAsDeclaredAlternateTargetConflicts()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        Func<Task> insert = () => Insert(data, store, id: 1);
        await insert.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate key*"); store.Count.Should().Be(1);
    }

    [Fact]
    public async Task FixtureKeyComparersNeverRewriteDeclaredConflictPredicates()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryWriteRow, string>("names", row => row.Name, InMemoryWriteRow.Copy, StringComparer.OrdinalIgnoreCase);
        store.Seed(Input("one", 1));
        Func<Task> insert = () => data.ExecuteInsertIfAbsentAsync(store, () => Input("ONE"), Conflict, row => row.Name);
        await insert.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate key*");
        store.Count.Should().Be(1); // Not AlreadyExists: ordinary conflict equality did not find a match.
    }

    [Fact]
    public async Task FactoriesRunOnlyOnTheirBranchAndUseSavedRowsNotPendingReservations()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var created = 0; var updated = 0;
        data.ConfigureGeneratedValues(store,
            onCreate: (row, rows) => { created++; row.Id = rows.Max(item => item.Id) + 1; },
            onUpdate: (row, rows) => { updated++; row.Version = rows.Max(item => item.Version) + 10; });
        await data.CreateAsync(store, () => Input("staged", 0), row => row.Name, save: false);
        data.PendingCount.Should().Be(1); created.Should().Be(1);
        (await Insert(data, store, "one", 0)).Status.Should().Be(CommandStatus.AlreadyExists);
        (await Upsert(data, store, matches: false)).Status.Should().Be(CommandStatus.NotMatched);
        created.Should().Be(1); updated.Should().Be(0);
        (await Upsert(data, store)).Value!.Version.Should().Be(12); updated.Should().Be(1);
        (await Insert(data, store, id: 0)).Value!.Id.Should().Be(2); created.Should().Be(2);
        data.PendingCount.Should().Be(1); // Its key reservation was not reused as saved data or flushed.
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate key*");
        data.PendingCount.Should().Be(1);
    }

    [Theory]
    [InlineData("InsertProjection")]
    [InlineData("UpdateProjection")]
    [InlineData("InsertFactory")]
    [InlineData("InsertTarget")]
    [InlineData("UpdateFactory")]
    [InlineData("UpdateKey")]
    [InlineData("UpdateTarget")]
    [InlineData("Setter")]
    [InlineData("Key")]
    [InlineData("ConflictTarget")]
    [InlineData("Reentry")]
    public async Task EveryFailureBeforeCommitPreservesSavedRowsAndEarlierStaging(string failure)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        data.ConfigureGeneratedValues(store,
            onCreate: (row, _) =>
            {
                if (row.Name == "staged") return;
                if (failure == "InsertFactory") throw new InvalidOperationException("factory");
                if (failure == "InsertTarget") row.Name = "changed";
            },
            onUpdate: (row, _) =>
            {
                if (failure == "UpdateFactory") throw new InvalidOperationException("factory");
                if (failure == "UpdateKey") row.Id++;
                if (failure == "UpdateTarget") row.Name = "changed";
            });
        await data.CreateAsync(store, () => Input("staged", 5), row => row.Name, false);
        var insert = failure.StartsWith("Insert", StringComparison.Ordinal);
        Func<Task> action = () => insert
            ? data.ExecuteInsertIfAbsentAsync(store, () => Input(), Conflict,
                row => failure == "InsertProjection" ? throw new InvalidOperationException("result") : row.Name)
            : data.ExecuteInsertOrUpdateAsync(store, () => Input("one"), Conflict, _ => true,
                (_, candidate) =>
                {
                    candidate.Version = 9;
                    if (failure == "Setter") throw new InvalidOperationException("setter");
                    if (failure == "Key") candidate.Id++;
                    if (failure == "ConflictTarget") candidate.Name = "changed";
                    if (failure == "Reentry") store.Find(1);
                }, row => failure == "UpdateProjection" ? throw new InvalidOperationException("result") : row.Name);
        await action.Should().ThrowAsync<InvalidOperationException>();
        store.Count.Should().Be(1); store.Find(1)!.Version.Should().Be(1); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); store.Find(5)!.Name.Should().Be("staged");
    }

    [Theory]
    [InlineData("Before")]
    [InlineData("Probe")]
    [InlineData("Predicate")]
    [InlineData("Result")]
    public async Task CancellationLeavesRowsAndStagingUnchanged(string timing)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); using var cancellation = new CancellationTokenSource();
        if (timing == "Before") cancellation.Cancel();
        Func<Task> action = () => data.ExecuteInsertOrUpdateAsync(store, () => Input("one"),
            (left, right) => { if (timing == "Probe") cancellation.Cancel(); return Conflict(left, right); },
            _ => { if (timing == "Predicate") cancellation.Cancel(); return false; },
            (_, row) => row.Version++, row => { if (timing == "Result") cancellation.Cancel(); return row.Name; }, cancellation.Token);
        if (timing == "Result") action = () => data.ExecuteInsertIfAbsentAsync(store, () => Input(), Conflict,
            row => { cancellation.Cancel(); return row.Name; }, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>(); store.Count.Should().Be(1); store.Find(1)!.Version.Should().Be(1);
    }

    [Fact]
    public async Task BaselinesAndCheckpointRollbackIncludeInsertsAndConditionalUpdates()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); data.CaptureBaseline();
        await using (await data.BeginTransactionAsync()) { await Insert(data, store); await Upsert(data, store); }
        store.Count.Should().Be(1); store.Find(1)!.Version.Should().Be(1);
        await using (var transaction = await data.BeginTransactionAsync())
        { await Insert(data, store); await Upsert(data, store); await transaction.CommitAsync(); }
        store.Count.Should().Be(2); store.Find(1)!.Version.Should().Be(2);
        data.RestoreBaseline(); store.Count.Should().Be(1); store.Find(1)!.Version.Should().Be(1);
    }

    [Fact]
    public async Task MappedInsertsReceiveNullMetadataUpdatesPreserveSavedMetadataAndEncodersAreAtomic()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryMappedWriteData, int>("public", row => row.Key, row => new(row.Key, row.Caption, row.Revision, row.Extra));
        var calls = new List<int>();
        var writes = data.MapWriteStore(store, row => new InMemoryWriteRow { Id = row.Key, Name = row.Caption, Version = row.Revision },
            (row, saved) => { calls.Add(row.Id); if (row.Name == "fail") throw new InvalidOperationException("encoder"); return new(row.Id, row.Name, row.Version, saved?.Extra ?? "created"); },
            InMemoryWriteRow.Copy, row => row.Id, (left, right) => left.Id == right.Id && left.Name == right.Name && left.Version == right.Version);
        store.Seed(new(1, "one", 1, "preserved"));
        (await Insert(data, writes)).Status.Should().Be(CommandStatus.Inserted); store.Find(2)!.Extra.Should().Be("created");
        (await data.ExecuteInsertOrUpdateAsync(writes, () => Input("one"), Conflict, _ => true,
            (original, candidate) => candidate.Version = original.Version + 1, row => row)).Status.Should().Be(CommandStatus.Updated);
        store.Find(1)!.Extra.Should().Be("preserved");
        Func<Task> fail = () => Insert(data, writes, "fail", 3); await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("encoder");
        store.Count.Should().Be(2); calls.Should().Equal(2, 1, 3);
        Func<Task> bypass = () => data.ExecuteInsertIfAbsentAsync(store, () => new InMemoryMappedWriteData(3, "bad", 1, ""),
            (left, right) => left.Caption == right.Caption, row => row);
        await bypass.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mapped write view*");
    }

    [Fact]
    public async Task ForeignStoresAndDisposedContextsCannotBeUsed()
    {
        var data = new InMemoryWriteDataContext(); var foreign = new InMemoryWriteDataContext(); var store = Store(foreign);
        Func<Task> wrong = () => Insert(data, store); await wrong.Should().ThrowAsync<ArgumentException>();
        await foreign.DisposeAsync(); Func<Task> disposed = () => Insert(foreign, store); await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }
}
