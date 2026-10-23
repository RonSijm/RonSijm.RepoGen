using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteRuntimeGuardTests
{
    [Fact]
    public async Task APopulatedEndpointGraphCannotBeAttachedThroughALinkFactory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new LinkWriteContext(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(LinkWriteTestHarness.Owners()); context.AddRange(LinkWriteTestHarness.Targets()); context.AddRange(LinkWriteTestHarness.Links());
        await context.SaveChangesAsync(); context.ChangeTracker.Clear(); context.ChangeTracker.LazyLoadingEnabled = false;
        var owner = await context.Set<LinkWriteOwner>().SingleAsync(row => row.TenantId == 1);
        var input = TrackedCollectionReconciler.PrepareInput<LinkWriteItem, (string, string)>([new("new", "id", "value")], item => (item.Region, item.Code), 5);
        Func<Task> apply = async () => await TrackedCollectionReconciler.ApplyLinksAsync(context, owner, nameof(LinkWriteOwner.Links), row => row.Links,
            input, row => (row.TargetRegion, row.TargetCode), item => new LinkWriteRow
            { TargetRegion = item.Region, TargetCode = item.Code, Target = new LinkWriteTarget { Region = item.Region, Code = item.Code, Name = "not allowed" } },
            null, null, false, false, query => query);
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*scalar join rows only*");
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        context.Set<LinkWriteTarget>().Count().Should().Be(4);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("getter")]
    [InlineData("end")]
    public void LinkInputCancellationIsObservedBeforePublicationEvenForTheLastCallback(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        if (phase == "before") cancellation.Cancel();
        string Key(LinkWriteItem item) { if (phase == "getter") cancellation.Cancel(); return item.Code; }
        IEnumerable<LinkWriteItem> Items()
        {
            yield return new("eu", "a", "value");
            if (phase == "end") cancellation.Cancel();
        }
        Action prepare = () => TrackedCollectionReconciler.PrepareInput(Items(), Key, 2, cancellation.Token);
        prepare.Should().Throw<OperationCanceledException>();
    }
}
