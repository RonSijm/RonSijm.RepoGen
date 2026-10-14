using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class TrackedCreateManyTests
{
    private static Patient Row(string name) => new() { Id = Guid.NewGuid(), Name = name, Email = name + "@example.com" };

    [Fact]
    public async Task AllFactoriesRunBeforeAnyTrackingAndStagingDefersTheResult()
    {
        await using var store = await SqliteTestStore.CreateAsync(); var results = 0;
        var staged = await TrackedCreateManyExecutor.ExecuteAsync<Patient, string, Guid>(store.Context, ["b", "a"], name =>
        {
            store.Context.ChangeTracker.Entries().Should().BeEmpty(); return Row(name);
        }, row => { results++; return row.Id; }, false, null, []);
        staged.Status.Should().Be(CommandStatus.Staged); results.Should().Be(0);
        (await store.Context.Patients.CountAsync()).Should().Be(2);
        var saved = await TrackedCreateManyExecutor.ExecuteAsync<Patient, string, string>(store.Context, ["d", "c"], Row, row => row.Name, true, null, []);
        saved.Value.Should().Equal("d", "c"); saved.AffectedRows.Should().Be(4);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("reused")]
    [InlineData("tracked")]
    [InlineData("cancel")]
    public async Task MappingFailuresDoNotStageAnyBatchRowsOrTouchEarlierPendingWork(string mode)
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var pending = store.Context.Add(Row("pending")).Entity; var shared = Row("shared"); using var cancellation = new CancellationTokenSource();
        Func<Task> call = () => TrackedCreateManyExecutor.ExecuteAsync<Patient, int, int>(store.Context, [1, 2], item =>
        {
            if (mode == "throw" && item == 2) throw new InvalidOperationException("mapping");
            if (mode == "null" && item == 2) return null!;
            if (mode == "reused") return shared;
            if (mode == "tracked" && item == 2) return pending;
            if (mode == "cancel" && item == 2) cancellation.Cancel();
            return Row("valid");
        }, row => row.Score, true, null, [], cancellation.Token);
        await call.Should().ThrowAsync<Exception>();
        store.Context.ChangeTracker.Entries().Should().ContainSingle(entry => ReferenceEquals(entry.Entity, pending));
        store.Context.Entry(pending).State.Should().Be(EntityState.Added); store.Context.ChangeTracker.LazyLoadingEnabled.Should().BeTrue();
        (await store.Context.Patients.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("cardinality")]
    [InlineData("cancel")]
    public async Task SavedResultFailuresAreDistinctFromPreSaveFailures(string mode)
    {
        await using var store = await SqliteTestStore.CreateAsync(); using var cancellation = new CancellationTokenSource();
        Func<Task> call = () => TrackedCreateManyExecutor.ExecuteAsync<Patient, string, string>(store.Context, ["a", "b"], Row, (rows, _) =>
        {
            rows.Should().OnlyContain(row => store.Context.Entry(row).State == EntityState.Unchanged);
            if (mode == "throw") throw new InvalidOperationException("result");
            if (mode == "cancel") cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<string>>(mode == "cardinality" ? [] : rows.Select(row => row.Name).ToArray());
        }, true, null, [], cancellation.Token);
        await call.Should().ThrowAsync<RepositorySavedResultException>(); (await store.Context.Patients.CountAsync()).Should().Be(4);
        store.Context.ChangeTracker.Entries().Should().HaveCount(2);
    }

    [Fact]
    public async Task UnsafeTransactionSettingsRejectBeforeCallingTheMapping()
    {
        await using var store = await SqliteTestStore.CreateAsync(); var calls = 0;
        store.Context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        Func<Task> call = () => TrackedCreateManyExecutor.ExecuteAsync<Patient, string, int>(store.Context, ["never"], name => { calls++; return Row(name); },
            row => row.Score, true, null, []);
        await call.Should().ThrowAsync<InvalidOperationException>(); calls.Should().Be(0);
        store.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyBatchDoesNotFlushOtherWorkAndSavedResultCannotBeStaged()
    {
        await using var store = await SqliteTestStore.CreateAsync(); store.Context.Add(Row("pending"));
        (await TrackedCreateManyExecutor.ExecuteAsync<Patient, string, int>(store.Context, [], Row, row => row.Score, true, null, [])).Status.Should().Be(CommandStatus.NoChanges);
        (await store.Context.Patients.CountAsync()).Should().Be(2);
        Func<Task> invalid = () => TrackedCreateManyExecutor.ExecuteAsync<Patient, string, int>(store.Context, ["never"], Row,
            (rows, _) => Task.FromResult<IReadOnlyList<int>>(rows.Select(row => row.Score).ToArray()), false, null, []);
        await invalid.Should().ThrowAsync<ArgumentException>(); store.Context.ChangeTracker.Entries().Should().HaveCount(1);
    }
}
