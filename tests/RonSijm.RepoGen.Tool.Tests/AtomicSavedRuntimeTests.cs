using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncResultSeesSavedRowInsideOperationTransactionAndCancellationPreservesOwnership(bool borrowed)
    {
        await using var context = MemoryCollectionTestHarness.Context();
        await AtomicSavedTestHarness.SeedAsync(context);
        await using var transaction = borrowed ? await context.Database.BeginTransactionAsync() : null;
        using var cancellation = new CancellationTokenSource();
        Func<Task> fail = () => AtomicWriteExecutor.ExecuteWithResultQueryAsync<MemoryCollectionChild, int>(context, new SqliteAtomicWriter(),
            () => new MemoryCollectionChild { ParentId = 1, Key = "new", Text = "new" }, ["ParentId", "Key", "Text", "Version", "Hidden"], ["ParentId", "Key"], null,
            async (saved, token) =>
            {
                context.Database.CurrentTransaction.Should().NotBeNull();
                token.Should().Be(cancellation.Token);
                var id = await context.Set<MemoryCollectionChild>().AsNoTracking().Where(row => row.Id == saved.Id).Select(row => row.Id).SingleAsync(token);
                cancellation.Cancel();
                return id;
            }, null, [], cancellation.Token);
        await fail.Should().ThrowAsync<OperationCanceledException>();
        (await context.Set<MemoryCollectionChild>().AnyAsync(row => row.Key == "new")).Should().Be(borrowed);
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
        }
        (await context.Set<MemoryCollectionChild>().AnyAsync(row => row.Key == "new")).Should().BeFalse();
    }

    [Fact]
    public async Task FixtureProjectionCannotMutateCommittedRowsThroughPreparedSnapshot()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data, "rows");
        var result = await data.ExecuteInsertIfAbsentWithSavedResultAsync(store, () => new MemoryCollectionParent { Id = 1, Name = "saved" },
            (row, candidate) => row.Id == candidate.Id, ["rows"], (row, snapshot) =>
            {
                var selected = snapshot.GetRows<MemoryCollectionParent>("rows").Single();
                selected.Name = "mutated snapshot";
                row.Name = "mutated identity";
                return selected;
            });
        result.Value!.Name.Should().Be("mutated snapshot");
        store.Find(1)!.Name.Should().Be("saved");
    }

    [Theory]
    [InlineData("result")]
    [InlineData("copy")]
    [InlineData("cancel")]
    [InlineData("reentry")]
    [InlineData("missing")]
    [InlineData("rootMissing")]
    public async Task FixtureFailuresNeverPublishPreparedRoot(string mode)
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data, "rows");
        var failCopy = false;
        var related = data.CreateStore<MemoryCollectionParent, int>("related", row => row.Id, row =>
            failCopy ? throw new InvalidOperationException("copy failed") : new() { Id = row.Id, Name = row.Name });
        related.Seed(new() { Id = 1 });
        failCopy = mode == "copy";
        using var cancellation = new CancellationTokenSource();
        string[] selection = mode switch
        {
            "missing" => ["rows", "not registered"],
            "rootMissing" => ["related"],
            _ => ["rows", "related"]
        };
        Func<Task> failure = () => data.ExecuteInsertIfAbsentWithSavedResultAsync(store, () => new MemoryCollectionParent { Id = 1 },
            (row, candidate) => row.Id == candidate.Id, selection, (row, snapshot) =>
            {
                if (mode == "cancel")
                {
                    cancellation.Cancel();
                    return 0;
                }
                if (mode == "reentry")
                {
                    return store.Count;
                }
                throw new InvalidOperationException("result failed");
            }, cancellation.Token);
        await failure.Should().ThrowAsync<Exception>();
        store.Count.Should().Be(0);
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task AlreadyExistsSkipsSnapshotAdaptersAndProjection()
    {
        var data = new InMemoryWriteDataContext();
        var store = Store(data, "rows");
        store.Seed(new() { Id = 1 });
        var result = await data.ExecuteInsertIfAbsentWithSavedResultAsync<MemoryCollectionParent, int, int>(store,
            () => new() { Id = 1 }, (row, candidate) => row.Id == candidate.Id, ["rows", "not registered"],
            (row, snapshot) => throw new InvalidOperationException("must not run"));
        result.Status.Should().Be(CommandStatus.AlreadyExists);
        result.HasValue.Should().BeFalse();
    }

    private static InMemoryStore<MemoryCollectionParent, int> Store(InMemoryWriteDataContext data, string name)
        => data.CreateStore<MemoryCollectionParent, int>(name, row => row.Id, row => new() { Id = row.Id, Name = row.Name });
}
