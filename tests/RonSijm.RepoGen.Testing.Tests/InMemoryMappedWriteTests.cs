using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryMappedWriteTests
{
    private static bool Same(InMemoryWriteRow left, InMemoryWriteRow right) => left.Id == right.Id && left.Name == right.Name && left.Version == right.Version;
    private static (InMemoryStore<InMemoryMappedWriteData, int> Data, InMemoryWriteStore<InMemoryWriteRow, int> Writes) Store(
        InMemoryWriteDataContext context, string name = "mapped", Func<InMemoryWriteRow, InMemoryMappedWriteData?, InMemoryMappedWriteData>? write = null,
        Func<InMemoryMappedWriteData, InMemoryWriteRow>? read = null)
    {
        var data = context.CreateStore<InMemoryMappedWriteData, int>(name, row => row.Key, row => new(row.Key, row.Caption, row.Revision, row.Extra));
        var writes = context.MapWriteStore(data, read ?? (row => new InMemoryWriteRow { Id = row.Key, Name = row.Caption, Version = row.Revision }),
            write ?? ((row, saved) => new(row.Id, row.Name, row.Version, saved?.Extra ?? "created")), InMemoryWriteRow.Copy, row => row.Id, Same);
        data.Seed(new(1, "old", 1, "preserved"));
        return (data, writes);
    }
    private static Task<CommandResult<string>> Patch(InMemoryWriteDataContext context, InMemoryWriteStore<InMemoryWriteRow, int> store,
        string title = "new", bool save = false, CancellationToken cancellationToken = default)
        => context.PatchAsync(store, row => row.Id == 1, row => { row.Name = title; return [nameof(row.Name)]; }, row => row.Version == 1, Same,
            (left, right) => left.Version == right.Version, (target, source, assigned) => { if (assigned.Contains(nameof(target.Name))) target.Name = source.Name; },
            row => row.Name, save, cancellationToken: cancellationToken);

    [Fact]
    public async Task PublicImmutableRowsAndEntityShapedStoresSharePendingSavesAndCheckpoints()
    {
        var context = new InMemoryWriteDataContext(); var (data, writes) = Store(context);
        var other = context.CreateStore<InMemoryWriteRow, int>("other", row => row.Id, InMemoryWriteRow.Copy);
        context.CaptureBaseline();
        (await Patch(context, writes)).Status.Should().Be(CommandStatus.Staged);
        await context.CreateAsync(other, () => new InMemoryWriteRow { Id = 2, Name = "other" }, row => row.Name, false);
        data.Find(1)!.Caption.Should().Be("old");
        (await context.SaveChangesAsync()).Should().Be(2);
        data.Find(1).Should().Be(new InMemoryMappedWriteData(1, "new", 1, "preserved"));
        await using (var transaction = await context.BeginTransactionAsync())
        {
            await Patch(context, writes, "rollback", save: true);
            await transaction.RollbackAsync();
        }
        data.Find(1)!.Caption.Should().Be("new");
        context.RestoreBaseline(); data.Find(1)!.Caption.Should().Be("old"); other.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("Null")]
    [InlineData("Key")]
    [InlineData("Scalar")]
    [InlineData("Throw")]
    public async Task EncodingFailuresCannotPartiallyCommitAnyStore(string failure)
    {
        var context = new InMemoryWriteDataContext();
        var (first, firstWrites) = Store(context, "first");
        var (second, secondWrites) = Store(context, "second", (row, saved) => failure switch
        {
            "Null" => null!, "Key" => new(row.Id + 1, row.Name, row.Version, "bad"),
            "Scalar" => new(row.Id, "lost", row.Version, "bad"), _ => throw new InvalidOperationException("encoding failed")
        });
        await Patch(context, firstWrites); await Patch(context, secondWrites);
        Func<Task> save = () => context.SaveChangesAsync(); await save.Should().ThrowAsync<InvalidOperationException>();
        first.Find(1)!.Caption.Should().Be("old"); second.Find(1)!.Caption.Should().Be("old"); context.PendingCount.Should().Be(2);
        context.DiscardChanges();
    }

    [Fact]
    public async Task SavedEncodingFailureRestoresTheNewCommandButRetainsUnrelatedStaging()
    {
        var context = new InMemoryWriteDataContext(); var (first, writes) = Store(context);
        var (second, invalid) = Store(context, "invalid", (_, _) => throw new InvalidOperationException("encoding"));
        await Patch(context, writes);
        Func<Task> action = () => Patch(context, invalid, save: true); await action.Should().ThrowAsync<InvalidOperationException>();
        context.PendingCount.Should().Be(1); first.Find(1)!.Caption.Should().Be("old"); second.Find(1)!.Caption.Should().Be("old");
        (await context.SaveChangesAsync()).Should().Be(1); first.Find(1)!.Caption.Should().Be("new");
    }

    [Fact]
    public async Task WritesReceiveLatestSavedExtrasAndNeverEncodeUntouchedRowsOrDeletes()
    {
        var encoded = new List<int>(); var context = new InMemoryWriteDataContext();
        var (data, writes) = Store(context, write: (row, saved) => { encoded.Add(row.Id); return new(row.Id, row.Name, row.Version, saved?.Extra ?? "created"); });
        data.Seed(new(2, "untouched", 1, "second"));
        await Patch(context, writes);
        data.Seed(new(1, "old", 1, "latest"), InMemoryDuplicateKeyBehavior.Replace);
        await context.SaveChangesAsync(); encoded.Should().Equal(1); data.Find(1)!.Extra.Should().Be("latest"); data.Find(2)!.Extra.Should().Be("second");
        await context.CreateAsync(writes, () => new InMemoryWriteRow { Id = 3, Name = "created", Version = 1 }, row => row.Name, true);
        data.Find(3)!.Extra.Should().Be("created"); encoded.Should().Equal(1, 3);
        await context.DeleteAsync(writes, row => row.Id == 3, _ => true, (_, _) => true, row => row.Name, true);
        encoded.Should().Equal(1, 3); data.Find(3).Should().BeNull();
    }

    [Fact]
    public async Task ReadKeyFailuresCancellationAndReentryDoNotPublishWrites()
    {
        var context = new InMemoryWriteDataContext(); var (data, writes) = Store(context, read: row => new() { Id = row.Key + 1, Name = row.Caption, Version = row.Revision });
        Func<Task> badKey = () => Patch(context, writes); await badKey.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read map changed*key*");
        context.PendingCount.Should().Be(0);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> cancel = () => Patch(context, writes, cancellationToken: cancelled.Token); await cancel.Should().ThrowAsync<OperationCanceledException>();
        data.Find(1)!.Caption.Should().Be("old");
        var reentryContext = new InMemoryWriteDataContext();
        var (_, reentrant) = Store(reentryContext, write: (row, saved) => { _ = reentryContext.PendingCount; return new(row.Id, row.Name, row.Version, "bad"); });
        Func<Task> reentry = () => Patch(reentryContext, reentrant, save: true); await reentry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*re-enter*");
        reentryContext.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task OneBackingStoreCannotHaveTwoIndependentPendingWriteViews()
    {
        var context = new InMemoryWriteDataContext(); var (data, _) = Store(context);
        Action duplicate = () => context.MapWriteStore(data, row => new InMemoryWriteRow { Id = row.Key }, (row, _) => new InMemoryMappedWriteData(row.Id, "", 1, ""),
            InMemoryWriteRow.Copy, row => row.Id, Same);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*already has a write view*");
        Func<Task> bypass = () => context.CreateAsync(data, () => new(2, "bypass", 1, ""), row => row.Caption, false);
        await bypass.Should().ThrowAsync<InvalidOperationException>().WithMessage("*configured mapped write view*");
        context.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDuringPublicRowEncodingPreventsEveryPreparedCommit()
    {
        using var cancellation = new CancellationTokenSource(); var context = new InMemoryWriteDataContext();
        var (first, firstWrites) = Store(context, "first");
        var (second, secondWrites) = Store(context, "second", (row, saved) =>
        {
            cancellation.Cancel(); return new(row.Id, row.Name, row.Version, saved!.Extra);
        });
        await Patch(context, firstWrites); await Patch(context, secondWrites);
        Func<Task> save = () => context.SaveChangesAsync(cancellation.Token);
        await save.Should().ThrowAsync<OperationCanceledException>();
        first.Find(1)!.Caption.Should().Be("old"); second.Find(1)!.Caption.Should().Be("old"); context.PendingCount.Should().Be(2);
        context.DiscardChanges();
    }
}
