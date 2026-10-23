using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryCreateManyTests
{
    private static (InMemoryWriteDataContext Data, InMemoryStore<InMemoryWriteRow, int> Store) Fixture()
    {
        var data = new InMemoryWriteDataContext();
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        data.ConfigureGeneratedValues(store, (row, existing) =>
        {
            if (row.Name == "generation-failure") throw new InvalidOperationException("factory");
            if (row.Id == 0) row.Id = existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1;
        }, null);
        store.Seed(new() { Id = 7, Name = "old" });
        return (data, store);
    }
    private static Task<CommandResult<IReadOnlyList<int>>> Create(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        IReadOnlyList<string> names, bool save = true, CancellationToken cancellationToken = default)
        => data.CreateManyAsync<InMemoryWriteRow, string, int, int>(store, names, name => new() { Name = name }, row => row.Id, save, cancellationToken);

    [Fact]
    public async Task StagingReservesWithinAndAcrossBatchesAndDefersAllResults()
    {
        var (data, store) = Fixture(); var resultCalls = 0;
        var staged = await data.CreateManyAsync<InMemoryWriteRow, string, int, int>(store, ["a", "b"], name => new() { Name = name },
            row => { resultCalls++; return row.Id; }, false);
        staged.Status.Should().Be(CommandStatus.Staged); staged.HasValue.Should().BeFalse(); resultCalls.Should().Be(0);
        store.Count.Should().Be(1); data.PendingCount.Should().Be(2);
        var saved = await Create(data, store, ["c", "d"]);
        saved.Value.Should().Equal(10, 11); saved.AffectedRows.Should().Be(4); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("generation")]
    [InlineData("duplicate")]
    [InlineData("reused")]
    [InlineData("null")]
    [InlineData("cancel")]
    public async Task PreparationFailuresAreAllOrNothingAndPreserveEarlierStaging(string mode)
    {
        var (data, store) = Fixture(); await Create(data, store, ["pending"], save: false);
        var shared = new InMemoryWriteRow { Id = 9 }; using var cancellation = new CancellationTokenSource();
        Func<Task> call = () => data.CreateManyAsync<InMemoryWriteRow, int, int, int>(store, [1, 2], item =>
        {
            if (mode == "factory" && item == 2) throw new InvalidOperationException("mapping");
            if (mode == "null" && item == 2) return null!;
            if (mode == "reused") return shared;
            if (mode == "cancel" && item == 2) cancellation.Cancel();
            return new() { Id = mode == "duplicate" ? 9 : 0, Name = mode == "generation" && item == 2 ? "generation-failure" : "valid" };
        }, row => row.Id, true, cancellation.Token);
        await call.Should().ThrowAsync<Exception>(); data.PendingCount.Should().Be(1); store.Count.Should().Be(1);
        (await Create(data, store, ["retry"])).Value.Should().Equal(9);
    }

    [Fact]
    public async Task FailedSharedSaveRestoresTheBatchButRetainsOtherPendingChanges()
    {
        var (data, store) = Fixture();
        var other = data.CreateStore<InMemoryWriteRow, int>("other", row => row.Id, InMemoryWriteRow.Copy);
        data.ConfigureGeneratedValues(other, null, (_, _) => throw new InvalidOperationException("other generation"));
        other.Seed(new() { Id = 1, Name = "old" });
        await data.PatchAsync(other, row => row.Id == 1, row => { row.Name = "pending"; return [nameof(row.Name)]; }, static _ => true,
            (left, right) => left.Name == right.Name, static (_, _) => true, (target, source, _) => target.Name = source.Name, row => row.Id, false);
        Func<Task> call = () => Create(data, store, ["a", "b"]);
        await call.Should().ThrowAsync<InvalidOperationException>();
        data.PendingCount.Should().Be(1); store.Count.Should().Be(1); other.Find(1)!.Name.Should().Be("old");
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("cardinality")]
    [InlineData("cancel")]
    public async Task ResultFailuresNeverUndoTheAlreadySavedRows(string mode)
    {
        var (data, store) = Fixture(); using var cancellation = new CancellationTokenSource();
        Func<Task> call = () => data.CreateManyAsync<InMemoryWriteRow, string, int, int>(store, ["a", "b"], name => new() { Name = name }, rows =>
        {
            if (mode == "throw") throw new InvalidOperationException("projection");
            if (mode == "cancel") cancellation.Cancel();
            return mode == "cardinality" ? [] : rows.Select(row => row.Id).ToArray();
        }, true, cancellation.Token);
        await call.Should().ThrowAsync<RepositorySavedResultException>(); store.Count.Should().Be(3); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task SavedCallbackSeesCommittedRowsOnceAndIsDetachedFromStorage()
    {
        var (data, store) = Fixture(); var calls = 0;
        var result = await data.CreateManyAsync<InMemoryWriteRow, string, int, int>(store, ["b", "a"], name => new() { Name = name }, rows =>
        {
            calls++; store.Count.Should().Be(3); data.PendingCount.Should().Be(0); rows[0].Name = "not saved";
            return rows.Select(row => row.Id).ToArray();
        }, true);
        calls.Should().Be(1); result.Value.Should().Equal(8, 9); store.Find(8)!.Name.Should().Be("b");
    }

    [Fact]
    public async Task EmptyInputDoesNotSaveAndSavedResultRequiresSave()
    {
        var (data, store) = Fixture(); await Create(data, store, ["pending"], save: false);
        (await Create(data, store, [])).Status.Should().Be(CommandStatus.NoChanges); data.PendingCount.Should().Be(1);
        Func<Task> invalid = () => data.CreateManyAsync<InMemoryWriteRow, string, int, int>(store, ["never"], name => new() { Name = name },
            rows => rows.Select(row => row.Id).ToArray(), false);
        await invalid.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(1);
    }
}
