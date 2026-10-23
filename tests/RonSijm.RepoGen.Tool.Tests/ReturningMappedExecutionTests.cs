using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningMappedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWritesReturnFieldIndexerAndShadowValuesWithoutEntityGetterBehavior(bool leases)
    {
        await using var fixture = await CreateAsync();
        var context = fixture.Context;
        var operations = Operations(Compile(context, Builder(leases: leases)), leases ? new ScopedDbContextLeaseProvider<ReturningMappedContext>(context) : context);
        const string title = "'; DROP TABLE ignored; --";
        var changed = await Call(operations, "RenameAsync", new(0, title));
        changed.AffectedRows.Should().Be(2);
        foreach (var item in changed.Items)
        {
            item.Title.Should().Be(title); item.Caption.Should().Be(title.ToUpperInvariant()); item.Audit.Should().Be(12);
        }
        var first = changed.Items.Single(row => row.Id == 1);
        first.State.Should().Be(ReturningState.Ready); first.Fingerprint.Should().Equal(1, 2, 3); first.Note.Should().Be("original");
        var second = changed.Items.Single(row => row.Id == 2);
        second.State.Should().BeNull(); second.Fingerprint.Should().BeNull(); second.Note.Should().BeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await Call(operations, "RenameAsync", new(0))).AffectedRows.Should().Be(0);
        (await Call(operations, "RemoveAsync", new(0, Version: 12))).Items.Should().BeEquivalentTo(changed.Items);
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task PendingFieldAndIndexerEditsAreNotSavedOrSubstitutedIntoNativeResults()
    {
        await using var fixture = await CreateAsync();
        var tracked = await fixture.Context.Rows.SingleAsync(row => row.Id == 1);
        fixture.Context.Entry(tracked).Property("state").CurrentValue = ReturningState.Pending;
        tracked[ReturningMappedContext.Note] = "pending";
        var result = await Call(Operations(Compile(fixture.Context), fixture.Context), "RenameAsync", new());
        result.Items.Single().State.Should().Be(ReturningState.Ready); result.Items.Single().Note.Should().Be("original");
        tracked.Title.Should().Be("item1"); tracked.AuditValue().Should().Be(11);
        tracked.StateValue().Should().Be(ReturningState.Pending); tracked[ReturningMappedContext.Note].Should().Be("pending");
        fixture.Context.ChangeTracker.Entries().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectionAndIndexerMaterializationFailuresRollBackOwnedAndSharedWrites(bool shared)
    {
        await using var fixture = await CreateAsync();
        await using var work = new RepositoryUnitOfWork<ReturningMappedContext>(fixture.Context);
        await using var transaction = shared ? await work.BeginTransactionAsync() : null;
        var operations = Operations(Compile(fixture.Context, Builder(leases: true)), work);
        Func<Task> fail = () => ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("Mapped projection failed:*");
        try
        {
            ReturningMappedRow.FailIndexerAssignment.Value = true;
            Func<Task> setter = () => Call(operations, "RenameAsync", new());
            await setter.Should().ThrowAsync<Exception>();
        }
        finally { ReturningMappedRow.FailIndexerAssignment.Value = false; }
        (await fixture.Context.Rows.AsNoTracking().Select(row => row.Title).ToListAsync()).Should().BeEquivalentTo("item1", "item2");
        if (shared)
        {
            (await Call(operations, "RenameAsync", new())).AffectedRows.Should().Be(1);
            await transaction!.RollbackAsync();
            (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Title.Should().Be("item1");
        }
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAndEmptyResultsDoNotInventValuesOrPersistChanges()
    {
        await using var fixture = await CreateAsync(); var operations = Operations(Compile(fixture.Context), fixture.Context);
        (await Call(operations, "RenameAsync", new(99))).Items.Should().BeEmpty();
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync();
        Func<Task> action = () => Call(operations, "RenameAsync", new(), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Title.Should().Be("item1");
    }
}
