using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryDataSnapshotTests
{
    [Fact]
    public void CaptureDetachesSelectedStoresDeduplicatesNamesAndNeverResetsBaselines()
    {
        var context = new InMemoryDataContext();
        var copies = 0;
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, row => { copies++; return FixtureRow.Copy(row); });
        var second = context.CreateStore<FixtureRow, int>("second", row => row.Id, FixtureRow.Copy);
        first.Seed(new() { Id = 1, Name = "before", Tags = ["original"] });
        second.Seed(new() { Id = 2 });
        context.CaptureBaseline();
        copies = 0;
        var snapshot = context.CaptureSnapshot("first", "second", "first");
        snapshot.StoreNames.Should().Equal("first", "second");
        copies.Should().Be(1);
        var rows = snapshot.GetRows<FixtureRow>("first");
        rows.Should().BeSameAs(snapshot.GetRows<FixtureRow>("first"));
        rows[0].Name = "detached";
        rows[0].Tags.Add("not stored");
        first.Find(1)!.Name.Should().Be("before");
        first.Find(1)!.Tags.Should().Equal("original");
        context.Clear();
        snapshot.GetRows<FixtureRow>("second").Should().ContainSingle().Which.Id.Should().Be(2);
        context.RestoreBaseline();
        first.Find(1)!.Name.Should().Be("before");
        second.Count.Should().Be(1);
        Action add = () => ((IList<FixtureRow>)rows).Add(new() { Id = 3 });
        add.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void EmptySelectionAndUnusedFailingStoresDoNotRunCopyAdapters()
    {
        var fail = false;
        var context = new InMemoryDataContext();
        var unused = context.CreateStore<FixtureRow, int>("unused", row => row.Id,
            row => fail ? throw new InvalidOperationException("unused copy") : FixtureRow.Copy(row));
        var selected = context.CreateStore<FixtureRow, int>("selected", row => row.Id, FixtureRow.Copy);
        unused.Seed(new() { Id = 1 });
        selected.Seed(new() { Id = 2 });
        fail = true;
        context.CaptureSnapshot().StoreNames.Should().BeEmpty();
        context.CaptureSnapshot("selected").GetRows<FixtureRow>("selected").Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing")]
    public void AllNamesAreValidatedBeforeAnyCopyAdapterRuns(string? name)
    {
        var copies = 0;
        var context = new InMemoryDataContext();
        var store = context.CreateStore<FixtureRow, int>("rows", row => row.Id, row => { copies++; return FixtureRow.Copy(row); });
        store.Seed(new() { Id = 1 });
        copies = 0;
        Action invalid = () => context.CaptureSnapshot("rows", name!);
        invalid.Should().Throw<Exception>();
        copies.Should().Be(0);
        Action noNames = () => context.CaptureSnapshot(null!);
        noNames.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SnapshotAccessChecksExactRowTypeAndCapturedNames()
    {
        var context = new InMemoryDataContext();
        context.CreateStore<FixtureRow, int>("rows", row => row.Id, FixtureRow.Copy);
        var snapshot = context.CaptureSnapshot("rows");
        Action missing = () => snapshot.GetRows<FixtureRow>("other");
        missing.Should().Throw<KeyNotFoundException>();
        Action caseSensitive = () => snapshot.GetRows<FixtureRow>("ROWS");
        caseSensitive.Should().Throw<KeyNotFoundException>();
        Action wrongType = () => snapshot.GetRows<Uri>("rows");
        wrongType.Should().Throw<InvalidOperationException>().WithMessage("*row type*");
        Action blank = () => snapshot.GetRows<FixtureRow>(" ");
        blank.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailingOrReentrantCopiesPublishNoSnapshotAndLeaveRowsAndBaselinesUntouched(bool reentrant)
    {
        var fail = false;
        var context = new InMemoryDataContext();
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, FixtureRow.Copy);
        var second = context.CreateStore<FixtureRow, int>("second", row => row.Id, row =>
        {
            if (fail)
            {
                if (reentrant) context.CaptureSnapshot("first");
                throw new InvalidOperationException("copy failure");
            }
            return FixtureRow.Copy(row);
        });
        first.Seed(new() { Id = 1, Name = "baseline" });
        second.Seed(new() { Id = 2 });
        context.CaptureBaseline();
        first.ReplaceWith([new() { Id = 3, Name = "current" }]);
        fail = true;
        Action capture = () => context.CaptureSnapshot("first", "second");
        capture.Should().Throw<InvalidOperationException>().WithMessage(reentrant ? "*cannot re-enter*" : "copy failure");
        fail = false;
        first.Find(3)!.Name.Should().Be("current");
        second.Find(2).Should().NotBeNull();
        context.RestoreBaseline();
        first.Find(1)!.Name.Should().Be("baseline");
        context.CaptureSnapshot("first", "second").StoreNames.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentArrangementCannotChangeAnotherSourceDuringCapture()
    {
        var context = new InMemoryDataContext();
        using var copying = new ManualResetEventSlim();
        using var finishCopy = new ManualResetEventSlim();
        var pause = false;
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, row =>
        {
            if (pause)
            {
                copying.Set();
                if (!finishCopy.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return FixtureRow.Copy(row);
        });
        var second = context.CreateStore<FixtureRow, int>("second", row => row.Id, FixtureRow.Copy);
        first.Seed(new() { Id = 1 });
        second.Seed(new() { Id = 2, Name = "before" });
        pause = true;
        var capture = Task.Run(() => context.CaptureSnapshot("first", "second"));
        copying.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var attempting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrange = Task.Run(() =>
        {
            attempting.SetResult();
            second.ReplaceWith([new() { Id = 2, Name = "after" }]);
        });
        try { await attempting.Task; }
        finally { finishCopy.Set(); }
        var snapshot = await capture;
        await arrange;
        snapshot.GetRows<FixtureRow>("second")[0].Name.Should().Be("before");
        second.Find(2)!.Name.Should().Be("after");
    }
}
