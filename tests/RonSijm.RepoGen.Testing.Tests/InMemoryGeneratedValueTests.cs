using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryGeneratedValueTests
{
    private static (InMemoryWriteDataContext Data, InMemoryStore<InMemoryWriteRow, int> Store) Fixture()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        data.ConfigureGeneratedValues(store, (row, existing) =>
        {
            if (row.Name == "fail") throw new ArgumentException("create generation failed");
            if (row.Id == 0) row.Id = checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
            row.Version = 1;
        }, (row, _) =>
        {
            if (row.Name == "fail") throw new ArgumentException("update generation failed");
            row.Version++;
        });
        store.Seed(new() { Id = 7, Name = "old", Version = 1 });
        return (data, store);
    }

    private static Task<CommandResult<InMemoryWriteRow>> Create(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        string name, bool save = false, int id = 0)
        => data.CreateAsync(store, () => new InMemoryWriteRow { Id = id, Name = name }, InMemoryWriteRow.Copy, save);

    private static Task<CommandResult<InMemoryWriteRow>> Patch(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        string name, bool save = false, bool force = true, int expected = 1)
        => data.PatchAsync(store, row => row.Id == 7, row => { row.Name = name; return [nameof(row.Name)]; }, row => row.Version == expected,
            (left, right) => left.Name == right.Name && left.Version == right.Version, (left, right) => left.Version == right.Version,
            (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; }, InMemoryWriteRow.Copy, save, force);

    [Fact]
    public async Task InsertAllocationIncludesSeedsAndPendingReservationsAndPreservesSuppliedKeys()
    {
        var (data, store) = Fixture();
        await Create(data, store, "eight"); await Create(data, store, "nine");
        store.Count.Should().Be(1); data.PendingCount.Should().Be(2);
        var result = await Create(data, store, "explicit", save: true, id: 100);
        result.AffectedRows.Should().Be(3); result.Value!.Id.Should().Be(100);
        store.Snapshot().Select(row => row.Id).Should().Equal(7, 8, 9, 100);
        (await Create(data, store, "next", save: true)).Value!.Id.Should().Be(101);
    }

    [Fact]
    public async Task UpdateValuesGenerateAtSaveOnceAndSavedResultsContainThem()
    {
        var (data, store) = Fixture();
        await Patch(data, store, "first"); await Patch(data, store, "second");
        store.Find(7)!.Version.Should().Be(1);
        var result = await Patch(data, store, "saved", save: true);
        result.Value!.Version.Should().Be(2); store.Find(7)!.Version.Should().Be(2);
        (await Patch(data, store, "stale", save: true)).Status.Should().Be(CommandStatus.Conflict);
        (await Patch(data, store, "saved", save: true, force: false, expected: 2)).AffectedRows.Should().Be(0);
        store.Find(7)!.Version.Should().Be(2);
    }

    [Fact]
    public async Task GenerationFailureCannotPartiallySaveAnotherStoreOrConsumeAnInsertReservation()
    {
        var (data, store) = Fixture();
        Func<Task> failCreate = () => Create(data, store, "fail");
        await failCreate.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0);
        (await Create(data, store, "ok", save: true)).Value!.Id.Should().Be(8);
        var other = data.CreateStore<InMemoryWriteRow, int>("other", row => row.Id, InMemoryWriteRow.Copy);
        await Create(data, other, "pending", id: 1);
        await Patch(data, store, "fail");
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<ArgumentException>();
        other.Count.Should().Be(0); store.Find(7)!.Version.Should().Be(1); data.PendingCount.Should().Be(2);
        data.DiscardChanges();
    }

    [Fact]
    public async Task BaselinesAndCheckpointsReplayRowDerivedAllocationWithoutExternalCounters()
    {
        var (data, store) = Fixture(); data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            (await Create(data, store, "rolled back", save: true)).Value!.Id.Should().Be(8);
            await Patch(data, store, "new", save: true);
        }
        store.Find(7)!.Version.Should().Be(1); store.Find(8).Should().BeNull();
        (await Create(data, store, "replayed", save: true)).Value!.Id.Should().Be(8);
        data.RestoreBaseline();
        (await Create(data, store, "baseline replay", save: true)).Value!.Id.Should().Be(8);
    }

    [Fact]
    public async Task GeneratedBatchResultsKeepInputOrderAndOriginalTokens()
    {
        var (data, store) = Fixture(); store.Seed(new() { Id = 9, Name = "nine", Version = 1 });
        var input = RepositoryBatchInputs.Prepare(new[] { new InMemoryBatchItem(9, "B"), new(7, "A") }, item => item.Id, 2);
        var result = await data.ExecuteBatchAsync(store, input, _ => true, row => row.Id,
            (row, item) => { row.Name = item.Name; return [nameof(row.Name)]; }, (row, item) => row.Version == item.ExpectedVersion,
            (left, right) => left.Name == right.Name && left.Version == right.Version, (left, right) => left.Version == right.Version,
            (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; }, InMemoryWriteRow.Copy,
            delete: false, save: true, forceWrite: true);
        result.Value!.Select(row => row.Id).Should().Equal(9, 7);
        result.Value!.Select(row => row.Version).Should().Equal(2, 2);
    }

    [Fact]
    public async Task RegistrationAndKeyMutationAreRejectedWithoutSavedChanges()
    {
        var (data, store) = Fixture();
        Action duplicate = () => data.ConfigureGeneratedValues(store, (_, _) => { });
        duplicate.Should().Throw<InvalidOperationException>();
        var foreign = new InMemoryWriteDataContext();
        Action wrong = () => foreign.ConfigureGeneratedValues(store, (_, _) => { });
        wrong.Should().Throw<ArgumentException>();
        var badStore = data.CreateStore<InMemoryWriteRow, int>("bad", row => row.Id, InMemoryWriteRow.Copy);
        badStore.Seed(new() { Id = 7, Name = "old", Version = 1 });
        data.ConfigureGeneratedValues(badStore, onUpdate: (row, _) => row.Id++);
        Func<Task> bad = () => Patch(data, badStore, "new", save: true);
        await bad.Should().ThrowAsync<InvalidOperationException>(); badStore.Find(7)!.Name.Should().Be("old"); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDuringGenerationAndReentrantFactoriesCannotCommitOrReserveRows()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        using var cancellation = new CancellationTokenSource();
        data.ConfigureGeneratedValues(store, (row, _) => { row.Id = 1; cancellation.Cancel(); });
        Func<Task> cancelled = () => data.CreateAsync(store, () => new InMemoryWriteRow(), InMemoryWriteRow.Copy, true, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>(); data.PendingCount.Should().Be(0); store.Count.Should().Be(0);
        var reentrant = data.CreateStore<InMemoryWriteRow, int>("reentrant", row => row.Id, InMemoryWriteRow.Copy);
        data.ConfigureGeneratedValues(reentrant, (row, _) => row.Id = store.Count + 1);
        Func<Task> invalid = () => Create(data, reentrant, "bad", save: true);
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*re-enter*");
        data.PendingCount.Should().Be(0); reentrant.Count.Should().Be(0);
    }

    [Fact]
    public async Task SavedMappingFailureRetainsGeneratedValuesUntilCheckpointRollback()
    {
        var (data, store) = Fixture();
        await using (await data.BeginTransactionAsync())
        {
            Func<Task> mapping = () => data.PatchAsync<InMemoryWriteRow, int, int>(store, row => row.Id == 7,
                row => { row.Name = "saved"; return [nameof(row.Name)]; }, row => row.Version == 1,
                (left, right) => left.Name == right.Name, (left, right) => left.Version == right.Version,
                (target, source, _) => target.Name = source.Name, _ => throw new ArgumentException("mapping"), true, true);
            await mapping.Should().ThrowAsync<RepositorySavedResultException>();
            store.Find(7)!.Version.Should().Be(2); data.PendingCount.Should().Be(0);
        }
        store.Find(7)!.Version.Should().Be(1);
    }
}
