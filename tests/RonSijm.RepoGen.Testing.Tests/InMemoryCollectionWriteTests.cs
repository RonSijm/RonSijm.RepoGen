using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryCollectionWriteTests
{
    private static (InMemoryWriteDataContext Data, InMemoryStore<InMemoryWriteRow, int> Parents, InMemoryStore<InMemoryCollectionRow, int> Children) Setup(bool collide = false)
    {
        var data = new InMemoryWriteDataContext();
        var parents = data.CreateStore<InMemoryWriteRow, int>("parents", row => row.Id, InMemoryWriteRow.Copy);
        var children = data.CreateStore<InMemoryCollectionRow, int>("children", row => row.Id, InMemoryCollectionRow.Copy);
        parents.Seed(new() { Id = 1, Name = "parent", Version = 1 });
        children.SeedRange([new() { Id = 1, OwnerId = 1, Key = "a", Text = "old" }, new() { Id = 2, OwnerId = 1, Key = "b", Text = "missing" },
            new() { Id = 3, OwnerId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new() { Id = 4, OwnerId = 2, Key = "other", Text = "other" }]);
        data.ConfigureGeneratedValues(children, (row, existing) => row.Id = collide ? 99 : existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        return (data, parents, children);
    }

    private static InMemoryCollectionWrite<InMemoryWriteRow> Collection(InMemoryWriteDataContext data, InMemoryStore<InMemoryCollectionRow, int> store,
        InMemoryCollectionItem[]? items, bool delete = true, bool reject = false, bool add = true, int maximum = 3,
        Func<InMemoryCollectionRow, InMemoryCollectionItem, IReadOnlyList<string>>? update = null, bool versions = true)
        => data.ReconcileCollection<InMemoryWriteRow, InMemoryCollectionRow, InMemoryCollectionItem, int, string>(store,
            items is null ? null : RepositoryBatchInputs.Prepare(items, item => item.Key, maximum), (parent, child) => child.OwnerId == parent.Id,
            child => !child.Hidden, child => child.Key, add ? item => new() { Key = item.Key, Text = item.Text } : null,
            (parent, child) => child.OwnerId = parent.Id,
            update ?? ((child, item) => { if (!item.Supplied) return []; child.Text = item.Text; return [nameof(child.Text)]; }),
            (child, item) => !versions || child.Version == item.ExpectedVersion,
            (left, right) => left.Key == right.Key && left.Text == right.Text && left.Version == right.Version && left.OwnerId == right.OwnerId && left.Hidden == right.Hidden,
            (left, right) => left.Version == right.Version,
            (target, source, assigned) => { if (assigned.Contains(nameof(target.Text))) target.Text = source.Text; }, maximum, delete, reject, versions);

    private static Task<CommandResult<string>> Run(InMemoryWriteDataContext data, InMemoryStore<InMemoryWriteRow, int> store,
        InMemoryCollectionWrite<InMemoryWriteRow>[] collections, bool save = true, bool patch = true, Func<InMemoryWriteRow, string>? result = null,
        Func<InMemoryWriteRow, bool>? expected = null, CancellationToken cancellationToken = default)
        => data.ReconcileAsync(store, row => row.Id == 1,
            row => { if (!patch) return []; row.Name = "changed"; return [nameof(row.Name)]; }, collections, expected ?? (row => row.Version == 1),
            (left, right) => left.Name == right.Name && left.Version == right.Version,
            (left, right) => left.Version == right.Version,
            (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; }, result ?? (row => row.Name), save,
            cancellationToken: cancellationToken);

    [Fact]
    public async Task SavedReconciliationAddsUpdatesDeletesAndNeverTouchesHiddenOrOtherOwners()
    {
        var (data, parents, children) = Setup();
        var result = await Run(data, parents, [Collection(data, children, [new("a", "new"), new("c", "new C"), new("d", "new D")])]);
        result.AffectedRows.Should().Be(5); result.Value.Should().Be("changed");
        children.Find(1)!.Text.Should().Be("new"); children.Find(2).Should().BeNull();
        children.Find(3)!.Text.Should().Be("hidden"); children.Find(4)!.OwnerId.Should().Be(2);
        children.Find(5)!.OwnerId.Should().Be(1); children.Find(6)!.Key.Should().Be("d"); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task StageKeepsSavedRowsDetachedAndSharedSaveIncludesOtherStores()
    {
        var (data, parents, children) = Setup();
        var other = data.CreateStore<InMemoryWriteRow, int>("audit", row => row.Id, InMemoryWriteRow.Copy);
        await data.CreateAsync(other, () => new() { Id = 9, Name = "audit" }, row => row.Name, false);
        (await Run(data, parents, [Collection(data, children, [new("a", "new"), new("c", "created")])], save: false)).Status.Should().Be(CommandStatus.Staged);
        children.Find(1)!.Text.Should().Be("old"); parents.Find(1)!.Name.Should().Be("parent");
        (await data.SaveChangesAsync()).Should().Be(5); other.Find(9)!.Name.Should().Be("audit");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentOrChildVersionConflictCannotStageAnyPartOfTheCommand(bool parent)
    {
        var (data, parents, children) = Setup();
        var result = await Run(data, parents, [Collection(data, children, [new("a", "new"), new("b", "stale", parent ? 1 : 99)])],
            expected: row => parent ? row.Version == 99 : row.Version == 1);
        result.Status.Should().Be(CommandStatus.Conflict); data.PendingCount.Should().Be(0);
        children.Find(1)!.Text.Should().Be("old"); parents.Find(1)!.Name.Should().Be("parent");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCollectionFailureRollsBackEarlierCollectionAndParent(bool conflict)
    {
        var (data, parents, children) = Setup();
        var second = data.CreateStore<InMemoryCollectionRow, int>("second", row => row.Id, InMemoryCollectionRow.Copy);
        second.Seed(new() { Id = 20, OwnerId = 1, Key = "last" });
        var plans = new[] { Collection(data, children, [new("a", "first")]), Collection(data, second, [new("last", "last", conflict ? 99 : 1)],
            update: (_, _) => throw new InvalidOperationException("late setter")) };
        if (conflict) (await Run(data, parents, plans)).Status.Should().Be(CommandStatus.Conflict);
        else { Func<Task> action = () => Run(data, parents, plans); await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("late setter"); }
        data.PendingCount.Should().Be(0); children.Find(1)!.Text.Should().Be("old"); parents.Find(1)!.Name.Should().Be("parent");
    }

    [Fact]
    public async Task CancellationAfterEarlierChildPreparationRestoresPendingAndSavedState()
    {
        var (data, parents, children) = Setup(); using var cancellation = new CancellationTokenSource();
        var plan = Collection(data, children, [new("a", "new"), new("b", "cancel")], update: (row, item) =>
            { row.Text = item.Text; if (item.Key == "b") cancellation.Cancel(); return [nameof(row.Text)]; });
        Func<Task> action = () => Run(data, parents, [plan], cancellationToken: cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>(); data.PendingCount.Should().Be(0); children.Find(1)!.Text.Should().Be("old");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepAndRejectMissingPoliciesAreExplicit(bool reject)
    {
        var (data, parents, children) = Setup();
        Func<Task> action = () => Run(data, parents, [Collection(data, children, [new("a", "new")], delete: false, reject: reject)]);
        if (reject) { await action.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*"); data.PendingCount.Should().Be(0); }
        else { await action(); children.Find(2).Should().NotBeNull(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unsupplied")]
    public async Task SkippedCollectionOrConditionalSettersCanYieldNoChanges(string? mode)
    {
        var (data, parents, children) = Setup();
        var result = await Run(data, parents, [Collection(data, children, mode is null ? null : [new("a", "unused", Supplied: false)], delete: false)], patch: false);
        result.Status.Should().Be(CommandStatus.NoChanges); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task EmptyInputCanDeleteVisibleChildrenWithoutPatchingParent()
    {
        var (data, parents, children) = Setup();
        (await Run(data, parents, [Collection(data, children, [])], patch: false)).AffectedRows.Should().Be(2);
        children.Snapshot().Select(row => row.Key).Should().BeEquivalentTo("hidden", "other"); parents.Find(1)!.Name.Should().Be("parent");
    }

    [Fact]
    public async Task DirtyOwnerChildrenIncludingHiddenStagingAreRejectedButOtherOwnersMayShareSave()
    {
        var (data, parents, children) = Setup();
        await data.DeleteAsync(children, row => row.Id == 3, static _ => true, (left, right) => left.Version == right.Version, row => row.Id, false);
        Func<Task> action = () => Run(data, parents, [Collection(data, children, [])]);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*"); data.PendingCount.Should().Be(1);
        data.DiscardChanges();
        await data.DeleteAsync(children, row => row.Id == 4, static _ => true, (left, right) => left.Version == right.Version, row => row.Id, false);
        (await Run(data, parents, [Collection(data, children, [])])).AffectedRows.Should().Be(4); children.Find(3).Should().NotBeNull();
    }

    [Fact]
    public async Task ExistingLimitAndUnknownKeysFailBeforeStaging()
    {
        var (data, parents, children) = Setup();
        Func<Task> limit = () => Run(data, parents, [Collection(data, children, [], maximum: 1)]);
        await limit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*item limit*");
        Func<Task> unknown = () => Run(data, parents, [Collection(data, children, [new("hidden", "must not match")], add: false)]);
        await unknown.Should().ThrowAsync<ArgumentException>().WithMessage("*unknown keys*"); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FixtureKeyCollisionCannotPartlyStageEarlierNewChildren()
    {
        var (data, parents, children) = Setup(collide: true);
        Func<Task> action = () => Run(data, parents, [Collection(data, children, [new("new A", "a"), new("new B", "b")])]);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Duplicate*key*"); data.PendingCount.Should().Be(0); children.Count.Should().Be(4);
    }

    [Fact]
    public async Task SharedSaveFailurePreservesUnrelatedStagingAndRestoresCommandOnly()
    {
        var (data, parents, children) = Setup();
        var other = data.CreateStore<InMemoryWriteRow, int>("other", row => row.Id, InMemoryWriteRow.Copy);
        other.Seed(new() { Id = 10, Version = 1 });
        await data.DeleteAsync(other, row => row.Id == 10, static _ => true, (left, right) => left.Version == right.Version, row => row.Id, false);
        other.Seed(new() { Id = 10, Version = 2 }, InMemoryDuplicateKeyBehavior.Replace);
        Func<Task> action = () => Run(data, parents, [Collection(data, children, [new("a", "new")])]);
        await action.Should().ThrowAsync<RepositoryWriteConflictException>(); data.PendingCount.Should().Be(1);
        parents.Find(1)!.Name.Should().Be("parent"); children.Find(1)!.Text.Should().Be("old");
    }

    [Fact]
    public async Task ResultFailureIsCommittedAndCheckpointRollbackRestoresAllStores()
    {
        var (data, parents, children) = Setup();
        Func<Task> action = () => Run(data, parents, [Collection(data, children, [new("a", "saved")])], result: _ => throw new InvalidOperationException("projection"));
        await action.Should().ThrowAsync<RepositorySavedResultException>(); data.PendingCount.Should().Be(0); children.Find(1)!.Text.Should().Be("saved");
        await using (await data.BeginTransactionAsync())
            await Run(data, parents, [Collection(data, children, [new("new", "rollback")])]);
        children.Find(1)!.Text.Should().Be("saved"); children.Find(5).Should().BeNull();
    }

    [Fact]
    public async Task CrossContextPlansAreRejectedWithoutLeakingStaging()
    {
        var (data, parents, children) = Setup(); var (other, _, otherChildren) = Setup();
        Func<Task> action = () => Run(data, parents, [Collection(data, children, []), Collection(other, otherChildren, [])]);
        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*different fixture context*"); data.PendingCount.Should().Be(0); other.PendingCount.Should().Be(0);
    }
}
