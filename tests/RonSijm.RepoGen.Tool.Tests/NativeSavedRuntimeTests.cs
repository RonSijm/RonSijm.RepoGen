using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterAsyncQueryRollsBackAndRetainsTransactionOwnership(bool borrowed)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await NativeSavedTestHarness.SeedAsync(context);
        await using var transaction = borrowed ? await context.Database.BeginTransactionAsync() : null;
        using var cancellation = new CancellationTokenSource();
        var source = context.Set<MemoryCollectionChild>().Where(row => row.ParentId == 1 && !row.Hidden)
            .Select(row => new { __repoGenKey0 = row.Id, __repoGenValue0 = "changed" });
        Func<Task> fail = () => ReturningWriteExecutor.ExecuteUpdateWithResultQueryAsync<MemoryCollectionChild, int>(context, new SqliteReturningWriter(),
            source, [new("Text", "__repoGenValue0")], 5, async (saved, token) =>
            {
                context.Database.CurrentTransaction.Should().NotBeNull();
                token.Should().Be(cancellation.Token);
                var query = SavedBatchQuery.Restrict(context.Set<MemoryCollectionChild>().AsNoTracking(), saved, (row, updated) => row.Id == updated.Id);
                var items = await query.Select(row => row.Id).ToListAsync(token);
                cancellation.Cancel();
                return items;
            }, null, [], cancellation.Token);
        await fail.Should().ThrowAsync<OperationCanceledException>();
        (await context.Set<MemoryCollectionChild>().Where(row => row.Id == 1).Select(row => row.Text).SingleAsync()).Should().Be("A");
        if (transaction is not null)
        {
            context.Database.CurrentTransaction.Should().BeSameAs(transaction);
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RuntimeRejectsResultCardinalityMismatchBeforeCommit(int count)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await NativeSavedTestHarness.SeedAsync(context);
        var source = context.Set<MemoryCollectionChild>().Where(row => row.ParentId == 1 && !row.Hidden)
            .Select(row => new { __repoGenKey0 = row.Id, __repoGenValue0 = "changed" });
        Func<Task> fail = () => ReturningWriteExecutor.ExecuteUpdateWithResultQueryAsync<MemoryCollectionChild, int>(context, new SqliteReturningWriter(),
            source, [new("Text", "__repoGenValue0")], 5, (rows, token) => Task.FromResult<IReadOnlyList<int>>(new int[count]), null, []);
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one value*");
        (await context.Set<MemoryCollectionChild>().Where(row => row.Id == 1).Select(row => row.Text).SingleAsync()).Should().Be("A");
    }

    [Fact]
    public async Task EmptyFixtureSelectionSkipsUnregisteredRelatedStoresAndResultCallback()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        var result = await data.ExecuteUpdateWithSavedResultsAsync<MemoryCollectionParent, int, int>(store, row => true, (row, changed) => changed.Name = "changed", 2,
            ["rows", "not registered"], (rows, snapshot) => throw new InvalidOperationException("must not run"));
        result.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projection")]
    [InlineData("copy")]
    [InlineData("count")]
    [InlineData("cancel")]
    [InlineData("reentry")]
    public async Task FixtureResultFailuresNeverPartiallyPublishRows(string mode)
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        store.SeedRange([new() { Id = 1, Name = "first" }, new() { Id = 2, Name = "second" }]);
        var failCopy = false;
        var related = data.CreateStore<MemoryCollectionParent, int>("related", row => row.Id,
            row => failCopy ? throw new InvalidOperationException("copy failed") : new() { Id = row.Id });
        related.Seed(new() { Id = 1 });
        failCopy = mode == "copy";
        using var cancellation = new CancellationTokenSource();
        Func<Task> failure = () => data.ExecuteUpdateWithSavedResultsAsync<MemoryCollectionParent, int, int>(store, row => true,
            (row, changed) => changed.Name = "changed", 2, ["rows", "related"], (rows, snapshot) =>
            {
                if (mode == "count")
                {
                    return [1];
                }
                if (mode == "cancel")
                {
                    cancellation.Cancel();
                    return [1, 2];
                }
                if (mode == "reentry")
                {
                    return [store.Count, store.Count];
                }
                throw new InvalidOperationException("result failed");
            }, cancellation.Token);
        await failure.Should().ThrowAsync<Exception>();
        store.Find(1)!.Name.Should().Be("first");
        store.Find(2)!.Name.Should().Be("second");
    }

    [Fact]
    public async Task FixtureEnumerationFailureCannotHappenAfterPublication()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        store.SeedRange([new() { Id = 1, Name = "first" }, new() { Id = 2, Name = "second" }]);
        Func<Task> failure = () => data.ExecuteUpdateWithSavedResultsAsync<MemoryCollectionParent, int, int>(store,
            row => true, (row, changed) => changed.Name = "changed", 2, ["rows"],
            (rows, snapshot) => new NativeSavedResultList<int>(2, ThrowDuringEnumeration()));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("result enumeration failed");
        store.Find(1)!.Name.Should().Be("first");
        store.Find(2)!.Name.Should().Be("second");
    }

    [Fact]
    public async Task FixtureUsesTheMaterializedCardinalityNotACallbackListsReportedCount()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        store.SeedRange([new() { Id = 1, Name = "first" }, new() { Id = 2, Name = "second" }]);
        Func<Task> failure = () => data.ExecuteUpdateWithSavedResultsAsync<MemoryCollectionParent, int, int>(store,
            row => true, (row, changed) => changed.Name = "changed", 2, ["rows"],
            (rows, snapshot) => new NativeSavedResultList<int>(2, [1]));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one value*");
        store.Find(1)!.Name.Should().Be("first");
        store.Find(2)!.Name.Should().Be("second");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseValidatesMaterializedResultsBeforeCommitting(bool throwDuringEnumeration)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await NativeSavedTestHarness.SeedAsync(context);
        var source = context.Set<MemoryCollectionChild>().Where(row => row.ParentId == 1 && !row.Hidden)
            .Select(row => new { __repoGenKey0 = row.Id, __repoGenValue0 = "changed" });
        Func<Task> failure = () => ReturningWriteExecutor.ExecuteUpdateWithResultQueryAsync<MemoryCollectionChild, int>(context, new SqliteReturningWriter(),
            source, [new("Text", "__repoGenValue0")], 5,
            (rows, token) => Task.FromResult<IReadOnlyList<int>>(new NativeSavedResultList<int>(2, throwDuringEnumeration ? ThrowDuringEnumeration() : [1])), null, []);
        await failure.Should().ThrowAsync<InvalidOperationException>();
        (await context.Set<MemoryCollectionChild>().Where(row => row.Id == 1).Select(row => row.Text).SingleAsync()).Should().Be("A");
    }

    private static IEnumerable<int> ThrowDuringEnumeration()
    {
        yield return 1;
        throw new InvalidOperationException("result enumeration failed");
    }

    [Fact]
    public async Task ReturnedSnapshotRowsCannotMutateCommittedFixtureRows()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data);
        store.SeedRange([new() { Id = 1 }, new() { Id = 2 }]);
        var result = await data.ExecuteUpdateWithSavedResultsAsync(store, row => true, (row, changed) => changed.Name = "changed", 2, ["rows"],
            (rows, snapshot) => snapshot.GetRows<MemoryCollectionParent>("rows"));
        result.Items[0].Name = "edited result";
        store.Snapshot().Should().OnlyContain(row => row.Name == "changed");
    }

    private static InMemoryStore<MemoryCollectionParent, int> Store(InMemoryWriteDataContext data)
        => data.CreateStore<MemoryCollectionParent, int>("rows", row => row.Id, row => new() { Id = row.Id, Name = row.Name });
}
