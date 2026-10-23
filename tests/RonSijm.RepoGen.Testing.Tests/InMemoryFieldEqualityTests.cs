using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryFieldEqualityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MixedFieldPoliciesSurvivePendingClonesAndRespectForcedWrites(bool force, bool stageFirst)
    {
        var data = new InMemoryWriteDataContext(); var store = Store(data, "first");
        if (stageFirst) await Patch(data, store, "OLD", "before", false, false);
        var result = await Patch(data, store, "OLD", "changed", true, force);
        result.AffectedRows.Should().Be(1);
        store.Find(1)!.Name.Should().Be(force ? "OLD" : "old"); store.Find(1)!.Unassigned.Should().Be("changed");
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonFailureCannotCommitAnyParticipatingStoreAndRetainsPriorPendingChanges(bool transaction)
    {
        var data = new InMemoryWriteDataContext(); var first = Store(data, "first"); var second = Store(data, "second");
        await using var checkpoint = transaction ? await data.BeginTransactionAsync() : null;
        await Patch(data, second, "second", "other", false, false);
        Func<Task> fail = () => Patch(data, first, "OLD", "changed", true, false, throws: true);
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("comparison failed");
        first.Find(1)!.Name.Should().Be("old"); second.Find(1)!.Name.Should().Be("old"); data.PendingCount.Should().Be(1);
        (await data.SaveChangesAsync()).Should().Be(1); second.Find(1)!.Name.Should().Be("second");
    }

    private static InMemoryStore<InMemoryWriteRow, int> Store(InMemoryWriteDataContext data, string name)
    {
        var store = data.CreateStore<InMemoryWriteRow, int>(name, row => row.Id, InMemoryWriteRow.Copy);
        store.Seed(new() { Id = 1, Name = "old", Version = 1, Unassigned = "before" }); return store;
    }
    private static Task<CommandResult<string>> Patch(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        string name, string other, bool save, bool force, bool throws = false)
        => data.PatchAsync(store, row => row.Id == 1, row => { row.Name = name; row.Unassigned = other; return [nameof(row.Name), nameof(row.Unassigned)]; },
            static _ => true, static (left, right) => string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) && left.Unassigned == right.Unassigned && left.Version == right.Version,
            static (left, right) => left.Version == right.Version,
            static (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; if (assigned.Contains(nameof(target.Unassigned))) target.Unassigned = source.Unassigned; },
            row => row.Name, save, force,
            (left, right, property) => throws ? throw new InvalidOperationException("comparison failed") : property == nameof(left.Name) && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
}
