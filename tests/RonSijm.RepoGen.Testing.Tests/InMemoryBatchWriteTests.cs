using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryBatchWriteTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data, string name = "rows")
    {
        var store = data.CreateStore<InMemoryWriteRow, int>(name, row => row.Id, InMemoryWriteRow.Copy);
        store.SeedRange([new() { Id = 1, Name = "one", Version = 1 }, new() { Id = 2, Name = "two", Version = 1 }]);
        return store;
    }
    private static Task<CommandResult<IReadOnlyList<string>>> Batch(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        InMemoryBatchItem[] items, bool save = true, bool delete = false, bool force = true,
        Func<InMemoryWriteRow, InMemoryBatchItem, IReadOnlyList<string>>? patch = null, Func<InMemoryWriteRow, string>? result = null,
        CancellationToken cancellationToken = default)
        => data.ExecuteBatchAsync(store, RepositoryBatchInputs.Prepare(items, item => item.Id, 3, cancellationToken), static _ => true, row => row.Id,
            patch ?? ((row, item) => { if (!item.Supplied) return Array.Empty<string>(); row.Name = item.Name; return [nameof(row.Name)]; }),
            (row, item) => row.Version == item.ExpectedVersion,
            static (left, right) => left.Name == right.Name && left.Version == right.Version && left.Unassigned == right.Unassigned,
            static (left, right) => left.Version == right.Version,
            static (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; }, result ?? (row => row.Name),
            delete, save, force, cancellationToken);

    [Fact]
    public async Task StageAndSharedSaveOwnAllRowsAndReturnValuesInInputOrder()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var other = Store(data, "other");
        (await Batch(data, store, [new(2, "B"), new(1, "A")], save: false)).Status.Should().Be(CommandStatus.Staged);
        store.Find(1)!.Name.Should().Be("one");
        data.PendingCount.Should().Be(2);
        await data.SaveChangesAsync();
        await data.CreateAsync(other, () => new InMemoryWriteRow { Id = 3, Name = "other" }, row => row.Name, false);
        var saved = await Batch(data, store, [new(2, "second"), new(1, "first")]);
        saved.AffectedRows.Should().Be(3);
        saved.Value.Should().Equal("second", "first");
        other.Find(3).Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrStaleLastItemCannotStageTheFirstItem(bool stale)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        var result = await Batch(data, store, [new(1, "must not apply"), stale ? new(2, "stale", 0) : new(99, "missing")], save: false);
        result.Status.Should().Be(stale ? CommandStatus.Conflict : CommandStatus.NotFound);
        data.PendingCount.Should().Be(0);
        store.Find(1)!.Name.Should().Be("one");
    }

    [Fact]
    public async Task SetterFailureAndCancellationDuringPreparationLeaveNoPendingBatch()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        Func<Task> fail = () => Batch(data, store, [new(1, "a"), new(2, "b")], patch: (row, item) =>
            { if (item.Id == 2) throw new InvalidOperationException("setter"); row.Name = item.Name; return [nameof(row.Name)]; });
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("setter");
        data.PendingCount.Should().Be(0);
        using var cancellation = new CancellationTokenSource();
        Func<Task> cancel = () => Batch(data, store, [new(1, "a"), new(2, "b")], patch: (row, item) =>
            { if (item.Id == 2) cancellation.Cancel(); row.Name = item.Name; return [nameof(row.Name)]; }, cancellationToken: cancellation.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        data.PendingCount.Should().Be(0);
        store.Find(1)!.Name.Should().Be("one");
    }

    [Fact]
    public async Task DirtyTargetsAreRejectedButUnchangedStagingIsNotMistakenForAModification()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        await Batch(data, store, [new(1, "dirty")], save: false);
        Func<Task> dirty = () => Batch(data, store, [new(1, "a"), new(2, "b")]);
        await dirty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*");
        data.PendingCount.Should().Be(1);
        data.DiscardChanges();
        await Batch(data, store, [new(1, "one")], save: false, force: false);
        (await Batch(data, store, [new(1, "next")])).Status.Should().Be(CommandStatus.Applied);
        store.Find(1)!.Name.Should().Be("next");
    }

    [Fact]
    public async Task OtherEntityConflictRetainsItsStagingAndCannotPartlySaveTheBatch()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        var other = data.CreateStore<InMemoryBinaryWriteRow, int>("binary", row => row.Id, InMemoryBinaryWriteRow.Copy);
        other.Seed(new() { Id = 10, Token = [1] });
        await data.DeleteAsync(other, row => row.Id == 10, static _ => true, (left, right) => InMemoryValueEquality.AreEqual(left.Token, right.Token), row => row.Id, false);
        other.Seed(new() { Id = 10, Token = [2] }, InMemoryDuplicateKeyBehavior.Replace);
        Func<Task> conflict = () => Batch(data, store, [new(1, "a"), new(2, "b")]);
        (await conflict.Should().ThrowAsync<RepositoryWriteConflictException>()).WithInnerException<InMemoryWriteConflictException>();
        data.PendingCount.Should().Be(1);
        store.Find(1)!.Name.Should().Be("one");
        store.Find(2)!.Name.Should().Be("two");
    }

    [Fact]
    public async Task EmptyUnsuppliedUnchangedAndMixedItemsHaveDistinctOutcomes()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        (await Batch(data, store, [])).Status.Should().Be(CommandStatus.NoChanges);
        (await Batch(data, store, [new(1, "ignored", Supplied: false)])).Status.Should().Be(CommandStatus.NoChanges);
        var unchanged = await Batch(data, store, [new(1, "one"), new(2, "two")], force: false);
        unchanged.Status.Should().Be(CommandStatus.Applied); unchanged.AffectedRows.Should().Be(0);
        var mixed = await Batch(data, store, [new(2, "ignored", Supplied: false), new(1, "new")], force: false);
        mixed.AffectedRows.Should().Be(1); mixed.Value.Should().Equal("two", "new");
    }

    [Fact]
    public async Task SavedProjectionFailureDoesNotReintroducePendingStateAndRollbackRestoresDeletes()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        Func<Task> mapping = () => Batch(data, store, [new(1, "saved")], result: _ => throw new InvalidOperationException("mapping"));
        await mapping.Should().ThrowAsync<RepositorySavedResultException>();
        store.Find(1)!.Name.Should().Be("saved"); data.PendingCount.Should().Be(0);
        await using (await data.BeginTransactionAsync())
        {
            var deleted = await Batch(data, store, [new(2, ""), new(1, "")], delete: true);
            deleted.Value.Should().Equal("two", "saved"); store.Count.Should().Be(0);
        }
        store.Count.Should().Be(2); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CompositeStoreKeysCanBeMatchedByAnotherUniqueScalar()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryWriteRow, (int, int)>("tuple", row => (row.Id, row.Version), InMemoryWriteRow.Copy);
        store.SeedRange([new() { Id = 1, Version = 1, Unassigned = "A", Name = "old A" }, new() { Id = 2, Version = 1, Unassigned = "B", Name = "old B" }]);
        var input = RepositoryBatchInputs.Prepare<InMemoryBatchItem, string>([new(2, "B"), new(1, "A")], item => item.Name, 3);
        var result = await data.ExecuteBatchAsync(store, input, static _ => true, row => row.Unassigned,
            (row, item) => { row.Name = "new " + item.Name; return [nameof(row.Name)]; }, static (_, _) => true,
            (left, right) => left.Name == right.Name, static (_, _) => true,
            (target, source, _) => target.Name = source.Name, row => row.Name, false, true);
        result.Value.Should().Equal("new B", "new A"); store.Find((1, 1))!.Name.Should().Be("new A");
    }
}
