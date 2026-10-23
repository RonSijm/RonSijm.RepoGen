using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryFilteredAtomicWriteTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data)
    {
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        store.Seed(new() { Id = 1, Name = "one", Version = 1 });
        return store;
    }

    [Fact]
    public async Task ResultFilterNeverConstrainsInsertionOrNonmatchingUpdateBranches()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var calls = 0;
        Task<CommandResult<int>> Upsert(string name, bool matches) => data.ExecuteInsertOrUpdateAsync(store,
            () => new InMemoryWriteRow { Id = 2, Name = name, Version = 1 }, (left, right) => left.Name == right.Name,
            _ => matches, (_, row) => row.Version++, row => row.Version, row => { calls++; return false; });
        (await Upsert("two", false)).Status.Should().Be(CommandStatus.Inserted);
        (await Upsert("one", false)).Status.Should().Be(CommandStatus.NotMatched);
        calls.Should().Be(0); store.Find(2)!.Version.Should().Be(1);
    }

    [Fact]
    public async Task ResultFilterObservesFactoryValuesOnACopyBeforeProjection()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var calls = new List<string>();
        data.ConfigureGeneratedValues(store, onUpdate: (row, _) => { calls.Add("factory"); row.Version = 9; });
        var result = await data.ExecuteInsertOrUpdateAsync(store, () => new InMemoryWriteRow { Id = 1 },
            (left, right) => left.Id == right.Id, _ => true, (_, row) => { calls.Add("update"); row.Version = 2; },
            row => { calls.Add("projection"); return row.Version; }, row => { calls.Add("filter"); row.Name = "not saved"; return row.Version == 9; });
        result.Value.Should().Be(9); calls.Should().Equal("update", "factory", "filter", "projection");
        store.Find(1)!.Name.Should().Be("one"); store.Find(1)!.Version.Should().Be(9);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("throws")]
    [InlineData("reentry")]
    public async Task ResultFilterFailurePreservesSavedRowsAndEarlierPendingWork(string mode)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var projected = false;
        await data.CreateAsync(store, () => new InMemoryWriteRow { Id = 5, Name = "pending" }, row => row.Name, false);
        Func<Task> action = () => data.ExecuteInsertOrUpdateAsync(store, () => new InMemoryWriteRow { Id = 1 },
            (left, right) => left.Id == right.Id, _ => true, (_, row) => row.Version = 9,
            row => { projected = true; return row.Version; }, row => mode switch
            {
                "throws" => throw new InvalidOperationException("filter failed"),
                "reentry" => store.Find(row.Id) is not null,
                _ => false
            });
        await action.Should().ThrowAsync<InvalidOperationException>(); projected.Should().BeFalse();
        store.Find(1)!.Version.Should().Be(1); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); store.Find(5)!.Name.Should().Be("pending");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationFromResultFilterPrecedesProjectionAndMissingResultErrors(bool visible)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var projected = false;
        using var cancellation = new CancellationTokenSource();
        Func<Task> action = () => data.ExecuteInsertOrUpdateAsync(store, () => new InMemoryWriteRow { Id = 1 },
            (left, right) => left.Id == right.Id, _ => true, (_, row) => row.Version = 9,
            row => { projected = true; return row.Version; }, _ => { cancellation.Cancel(); return visible; }, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>(); projected.Should().BeFalse(); store.Find(1)!.Version.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointsRestoreSuccessfulFilteredUpdatesAndUnfilteredInserts()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        await using (var transaction = await data.BeginTransactionAsync())
        {
            (await data.ExecuteInsertOrUpdateAsync(store, () => new InMemoryWriteRow { Id = 1 },
                (left, right) => left.Id == right.Id, _ => true, (_, row) => row.Version++, row => row.Version, row => row.Version < 3)).Status.Should().Be(CommandStatus.Updated);
            (await data.ExecuteInsertOrUpdateAsync(store, () => new InMemoryWriteRow { Id = 2 },
                (left, right) => left.Id == right.Id, _ => false, (_, row) => row.Version++, row => row.Version, _ => false)).Status.Should().Be(CommandStatus.Inserted);
            await transaction.RollbackAsync();
        }
        store.Count.Should().Be(1); store.Find(1)!.Version.Should().Be(1);
    }
}
