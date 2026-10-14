using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryBatchSavedResultTests
{
    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data)
    {
        var store = data.CreateStore<InMemoryWriteRow, int>("rows", row => row.Id, InMemoryWriteRow.Copy);
        store.SeedRange([new() { Id = 1, Name = "one", Version = 1 }, new() { Id = 2, Name = "two", Version = 1 }]);
        return store;
    }
    private static Task<CommandResult<IReadOnlyList<string>>> Batch(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        Func<IReadOnlyList<InMemoryWriteRow>, IReadOnlyList<string>> result, bool save = true, bool delete = false,
        InMemoryBatchItem[]? items = null, CancellationToken cancellationToken = default)
        => data.ExecuteBatchAsync(store, RepositoryBatchInputs.Prepare(items ?? [new(2, "second"), new(1, "first")], item => item.Id, 3, cancellationToken),
            static _ => true, row => row.Id,
            (row, item) => { if (!item.Supplied) return Array.Empty<string>(); row.Name = item.Name; return [nameof(row.Name)]; },
            (row, item) => row.Version == item.ExpectedVersion, (left, right) => left.Name == right.Name,
            (left, right) => left.Version == right.Version, (target, source, _) => target.Name = source.Name,
            result, delete, save, cancellationToken: cancellationToken);

    [Fact]
    public async Task CallbackRunsOnceAfterSavingOutsideTheWriteLockAndRetainsInputOrder()
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var calls = 0;
        var result = await Batch(data, store, rows =>
        {
            calls++; data.PendingCount.Should().Be(0); rows.Select(row => row.Id).Should().Equal(2, 1);
            return InMemorySavedBatchResults.Project(rows, store.Snapshot(), row => [row.Id], row => row.Name);
        });
        calls.Should().Be(1); result.Value.Should().Equal("second", "first"); result.AffectedRows.Should().Be(2);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cardinality")]
    [InlineData("cancel")]
    [InlineData("cancel-returned")]
    public async Task PostSaveResultErrorsKeepSavedRowsAndDoNotReintroduceStaging(string mode)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); using var cancellation = new CancellationTokenSource();
        Func<Task> call = () => Batch(data, store, _ =>
        {
            if (mode == "cardinality") return [];
            if (mode == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            if (mode == "cancel-returned") { cancellation.Cancel(); return ["one", "two"]; }
            throw new InvalidOperationException("result failed");
        }, cancellationToken: cancellation.Token);
        await call.Should().ThrowAsync<RepositorySavedResultException>();
        store.Find(1)!.Name.Should().Be("first"); store.Find(2)!.Name.Should().Be("second"); data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task StagingAndDeletionRejectSavedResultsBeforeChangingAnyRows(bool save, bool delete)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data);
        Func<Task> call = () => Batch(data, store, _ => [], save, delete);
        await call.Should().ThrowAsync<ArgumentException>(); data.PendingCount.Should().Be(0); store.Find(1)!.Name.Should().Be("one");
    }

    [Theory]
    [InlineData("empty", CommandStatus.NoChanges)]
    [InlineData("missing", CommandStatus.NotFound)]
    [InlineData("stale", CommandStatus.Conflict)]
    [InlineData("unassigned", CommandStatus.NoChanges)]
    public async Task NonSavingOutcomesNeverCallTheSavedProjection(string mode, CommandStatus status)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data); var calls = 0;
        InMemoryBatchItem[] items = mode switch { "empty" => [], "missing" => [new(9, "missing")], "stale" => [new(1, "stale", 99)], _ => [new(1, "unassigned", Supplied: false)] };
        var result = await Batch(data, store, _ => { calls++; return []; }, items: items);
        result.Status.Should().Be(status); result.HasValue.Should().BeFalse(); calls.Should().Be(0); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CoordinatedSnapshotCorrelationUsesCompleteStructuralKeysAndIgnoresUnrelatedRows()
    {
        var result = InMemorySavedBatchResults.Project<byte[], byte>([[2], [1]], [[1], [3], [2]], row => [row], row => row[0]);
        result.Should().Equal(2, 1);
        Action missing = () => InMemorySavedBatchResults.Project<byte[], byte>([[2]], [[1]], row => [row], row => row[0]);
        missing.Should().Throw<InvalidOperationException>();
    }
}
