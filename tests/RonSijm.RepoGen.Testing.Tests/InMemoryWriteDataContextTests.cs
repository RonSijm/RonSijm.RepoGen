using System.Data;
using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryWriteDataContextTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data, string name = "rows")
    {
        var store = data.CreateStore<InMemoryWriteRow, int>(name, row => row.Id, InMemoryWriteRow.Copy);
        store.Seed(new() { Id = 1, Name = "old", Version = 1, Unassigned = "preserved" });
        return store;
    }
    private static Task<CommandResult<string>> Patch(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        string name, bool save = false, int expected = 1, bool assign = true, bool force = true, CancellationToken cancellationToken = default)
        => data.PatchAsync(store, row => row.Id == 1, row =>
        {
            if (!assign) return Array.Empty<string>();
            row.Name = name;
            return [nameof(row.Name)];
        }, row => row.Version == expected,
        static (left, right) => left.Name == right.Name && left.Version == right.Version && left.Unassigned == right.Unassigned,
        static (left, right) => left.Version == right.Version,
        static (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; },
        row => row.Name, save, force, cancellationToken);

    [Fact]
    public async Task StagingIsInvisibleToSnapshotsAndSharedSaveCommitsAllStores()
    {
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = Store(data, "second");
        (await Patch(data, first, "one")).Status.Should().Be(CommandStatus.Staged);
        (await Patch(data, second, "two")).HasValue.Should().BeFalse();
        data.PendingCount.Should().Be(2);
        first.Find(1)!.Name.Should().Be("old");
        second.Find(1)!.Name.Should().Be("old");
        (await data.SaveChangesAsync()).Should().Be(2);
        first.Find(1)!.Name.Should().Be("one");
        second.Find(1)!.Name.Should().Be("two");
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task SavedCommandIncludesTheOtherRepositoryStagedCount()
    {
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = Store(data, "second");
        await Patch(data, second, "two");
        var result = await Patch(data, first, "one", save: true);
        result.Status.Should().Be(CommandStatus.Applied);
        result.AffectedRows.Should().Be(2);
        result.Value.Should().Be("one");
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsuppliedAndUnchangedAssignmentsHaveDistinctOutcomes(bool assign)
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        var result = await Patch(data, store, "old", save: true, assign: assign, force: false);
        result.Status.Should().Be(assign ? CommandStatus.Applied : CommandStatus.NoChanges);
        result.AffectedRows.Should().Be(0);
        result.HasValue.Should().Be(assign);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task ExpectedTokenUsesOriginalStateWhileMultiplePatchesAccumulate()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        await data.PatchAsync(store, row => row.Id == 1, row => { row.Version = 2; return [nameof(row.Version)]; },
            row => row.Version == 1, static (left, right) => left.Version == right.Version, static (left, right) => left.Version == right.Version,
            static (target, source, assigned) => { if (assigned.Contains(nameof(target.Version))) target.Version = source.Version; if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; },
            row => row.Name, false, true);
        (await Patch(data, store, "new")).Status.Should().Be(CommandStatus.Staged);
        (await data.SaveChangesAsync()).Should().Be(1);
        store.Find(1)!.Version.Should().Be(2);
        store.Find(1)!.Name.Should().Be("new");
    }

    [Fact]
    public async Task StaleInputAndMissingRowsDoNotStageAnything()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        (await Patch(data, store, "bad", expected: 0)).Status.Should().Be(CommandStatus.Conflict);
        var missing = await data.DeleteAsync(store, row => row.Id == 99, static _ => true, static (_, _) => true, row => row.Name, true);
        missing.Status.Should().Be(CommandStatus.NotFound);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateKeysAreExplicitDuplicatesFailAndDeleteReturnsTheRemovedValue()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        var input = new InMemoryWriteRow { Id = 2, Name = "created" };
        (await data.CreateAsync(store, () => input, row => row.Name, false)).Status.Should().Be(CommandStatus.Staged);
        input.Name = "changed caller";
        Func<Task> duplicate = () => data.CreateAsync(store, () => new InMemoryWriteRow { Id = 2 }, row => row.Name, false);
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate key*");
        await data.SaveChangesAsync();
        store.Find(2)!.Name.Should().Be("created");
        var deleted = await data.DeleteAsync(store, row => row.Id == 2, static _ => true, static (_, _) => true, row => row.Name, true);
        deleted.Value.Should().Be("created");
        store.Find(2).Should().BeNull();
    }

    [Fact]
    public async Task SharedSaveConflictRetainsOtherPendingChangesAndDoesNotCommitThisCommand()
    {
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = Store(data, "second");
        await Patch(data, second, "pending");
        second.Seed(new() { Id = 1, Name = "concurrent", Version = 2 }, InMemoryDuplicateKeyBehavior.Replace);
        Func<Task> action = () => Patch(data, first, "must not save", save: true);
        (await action.Should().ThrowAsync<RepositoryWriteConflictException>()).WithInnerException<InMemoryWriteConflictException>();
        first.Find(1)!.Name.Should().Be("old");
        second.Find(1)!.Name.Should().Be("concurrent");
        data.PendingCount.Should().Be(1);
        data.DiscardChanges();
    }

    [Fact]
    public async Task PreparationFailureAndCancellationCannotPartiallyCommitStores()
    {
        var fail = false;
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = data.CreateStore<InMemoryWriteRow, int>("second", row => row.Id, row => fail ? throw new InvalidOperationException("copy") : InMemoryWriteRow.Copy(row));
        second.Seed(new() { Id = 1, Version = 1 });
        await Patch(data, first, "pending");
        await Patch(data, second, "pending");
        fail = true;
        Func<Task> save = () => data.SaveChangesAsync();
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("copy");
        first.Find(1)!.Name.Should().Be("old");
        data.PendingCount.Should().Be(2);
        fail = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> canceled = () => data.SaveChangesAsync(cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        data.PendingCount.Should().Be(2);
        (await data.SaveChangesAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UnassignedColumnsSurviveConcurrentFixtureArrangementWithoutTokenChanges()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        await Patch(data, store, "approved");
        store.Seed(new() { Id = 1, Name = "old", Version = 1, Unassigned = "concurrent" }, InMemoryDuplicateKeyBehavior.Replace);
        await data.SaveChangesAsync();
        store.Find(1)!.Name.Should().Be("approved");
        store.Find(1)!.Unassigned.Should().Be("concurrent");
    }

    [Fact]
    public async Task ResultFailureIsReportedAfterSaveWithoutReintroducingPendingWrites()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        Func<Task> action = () => data.CreateAsync<InMemoryWriteRow, int, string>(store, () => new() { Id = 2 },
            _ => throw new InvalidOperationException("mapping"), true);
        (await action.Should().ThrowAsync<RepositorySavedResultException>()).WithInnerException<InvalidOperationException>();
        store.Find(2).Should().NotBeNull();
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task TransactionRollbackRestoresSavedRowsAndDiscardsUnsavedWritesAcrossRepositories()
    {
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = Store(data, "second");
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
        {
            await Patch(data, first, "saved", save: true);
            await Patch(data, second, "staged");
            first.Find(1)!.Name.Should().Be("saved");
            data.PendingCount.Should().Be(1);
            Action arrange = () => first.Seed(new() { Id = 3 });
            arrange.Should().Throw<InvalidOperationException>().WithMessage("*inside a simulated transaction*");
        }
        first.Find(1)!.Name.Should().Be("old");
        second.Find(1)!.Name.Should().Be("old");
        data.PendingCount.Should().Be(0);
        data.RestoreBaseline();
    }

    [Fact]
    public async Task TransactionCommitDoesNotImplicitlySaveAndResetRequiresExplicitDiscard()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        await using (var transaction = await data.BeginTransactionAsync())
        {
            await Patch(data, store, "staged");
            await transaction.CommitAsync();
        }
        store.Find(1)!.Name.Should().Be("old");
        data.PendingCount.Should().Be(1);
        Action reset = data.RestoreBaseline;
        reset.Should().Throw<InvalidOperationException>().WithMessage("*discard pending*");
        await data.SaveChangesAsync();
        store.Find(1)!.Name.Should().Be("staged");
    }

    [Fact]
    public async Task MutableRequestsAreCopiedBeforeStageAndBinaryTokensCompareByContents()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryBinaryWriteRow, int>("binary", row => row.Id, InMemoryBinaryWriteRow.Copy);
        store.Seed(new() { Id = 1, Data = [1], Token = [1] });
        byte[] input = [2];
        byte[] token = [2];
        byte[] expected = [1];
        var staged = await data.PatchAsync(store, row => row.Id == 1, row => { row.Data = input; row.Token = token; return [nameof(row.Data), nameof(row.Token)]; },
            row => InMemoryValueEquality.AreEqual(row.Token, expected),
            static (left, right) => InMemoryValueEquality.AreEqual(left.Data, right.Data) && InMemoryValueEquality.AreEqual(left.Token, right.Token),
            static (left, right) => InMemoryValueEquality.AreEqual(left.Token, right.Token),
            static (target, source, _) => { target.Data = source.Data; target.Token = source.Token; }, row => row.Data, false, true);
        staged.Status.Should().Be(CommandStatus.Staged);
        input[0] = 99;
        token[0] = 99;
        await data.SaveChangesAsync();
        store.Find(1)!.Data.Should().Equal(2);
        store.Find(1)!.Token.Should().Equal(2);
    }

    [Fact]
    public async Task CancellationDuringTheLastPreparationStillPreventsEveryCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var cancel = false;
        var data = new InMemoryWriteDataContext();
        var first = Store(data, "first");
        var second = data.CreateStore<InMemoryWriteRow, int>("second", row => row.Id, row => { if (cancel) cancellation.Cancel(); return InMemoryWriteRow.Copy(row); });
        second.Seed(new() { Id = 1, Version = 1 });
        await Patch(data, first, "pending");
        await Patch(data, second, "pending");
        cancel = true;
        Func<Task> save = () => data.SaveChangesAsync(cancellation.Token);
        await save.Should().ThrowAsync<OperationCanceledException>();
        first.Find(1)!.Name.Should().Be("old");
        data.PendingCount.Should().Be(2);
        cancel = false;
        second.Find(1)!.Name.Should().Be("");
    }

    [Fact]
    public async Task PatchesCannotChangeKeysOrReenterTheirDataContext()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        Func<Task> changeKey = () => data.PatchAsync(store, row => row.Id == 1, row => { row.Id = 2; return [nameof(row.Id)]; },
            static _ => true, static (_, _) => false, static (_, _) => true, static (_, _, _) => { }, row => row.Name, false);
        await changeKey.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed a fixture key*");
        Func<Task> reenter = () => data.CreateAsync(store, () => { store.Remove(1); return new InMemoryWriteRow { Id = 3 }; }, row => row.Name, true);
        await reenter.Should().ThrowAsync<InvalidOperationException>().WithMessage("*re-enter*");
        data.PendingCount.Should().Be(0);
        store.Find(1).Should().NotBeNull();
    }

    [Fact]
    public async Task DetachedSavedValuesAndWrongContextStoresCannotMutateAnotherFixture()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        var result = await data.CreateAsync(store, () => new InMemoryWriteRow { Id = 2, Name = "saved" }, row => row, true);
        result.Value!.Name = "caller";
        store.Find(2)!.Name.Should().Be("saved");
        var other = new InMemoryWriteDataContext();
        Func<Task> wrong = () => other.CreateAsync(store, () => new InMemoryWriteRow { Id = 3 }, row => row.Name, true);
        await wrong.Should().ThrowAsync<ArgumentException>().WithMessage("*different fixture*");
        other.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task TransactionGuardsAndDisposalHaveHonestLifetimeBoundaries()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        Func<Task> isolation = async () => { await data.BeginTransactionAsync(IsolationLevel.Serializable); };
        await isolation.Should().ThrowAsync<NotSupportedException>();
        await Patch(data, store, "pending");
        Func<Task> dirty = async () => { await data.BeginTransactionAsync(); };
        await dirty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending*");
        data.DiscardChanges();
        await using var transaction = await data.BeginTransactionAsync();
        Func<Task> nested = async () => { await data.BeginTransactionAsync(); };
        await nested.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already active*");
        await Patch(data, store, "saved", true);
        await data.DisposeAsync();
        await data.DisposeAsync();
        Action read = () => store.Snapshot();
        read.Should().Throw<ObjectDisposedException>();
    }
}
