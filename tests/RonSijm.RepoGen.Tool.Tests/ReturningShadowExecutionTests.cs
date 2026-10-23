using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningShadowTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ReturningWriteTests;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAndDeleteReturnDetachedShadowValuesWithFiltersConvertersNullsAndCompositeKeys(bool leases)
    {
        await using var fixture = await CreateAsync();
        var context = fixture.Context;
        var assembly = Compile(context, Builder(leases: leases));
        var operations = Operations(assembly, leases ? new ScopedDbContextLeaseProvider<ReturningShadowContext>(context) : context);
        operations.Should().BeAssignableTo<IReturningShadowWrites>();
        var writes = (IReturningShadowWrites)operations;
        const string suffix = "'; DROP TABLE ignored; --";
        var changed = await writes.AdjustAsync(new("item", 2, suffix, 1));
        changed.AffectedRows.Should().Be(2);
        changed.Items.Select(row => row.Id).Should().BeEquivalentTo<int>([1, 2]);
        foreach (var row in changed.Items)
        {
            row.TenantId.Should().Be(1); row.Revision.Should().Be(2);
            row.Text.Should().Be("item" + row.Id + suffix);
            row.Normalized.Should().Be(row.Text.ToUpperInvariant());
            row.Quantity.Should().Be(row.Credit + 2);
            row.ComputedVersion.Should().Be(row.Quantity + 10); row.Echo.Should().Be(suffix);
        }
        var first = changed.Items.Single(row => row.Id == 1);
        first.Status.Should().Be(ReturningState.Ready); first.OptionalNote.Should().BeNull();
        first.Fingerprint.Should().Equal(1, 2, 3);
        var second = changed.Items.Single(row => row.Id == 2);
        second.Status.Should().BeNull(); second.OptionalNote.Should().Be("original"); second.Fingerprint.Should().BeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();
        var hidden = await context.Rows.IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == 3 || row.TenantId == 2).ToListAsync();
        hidden.Should().HaveCount(2).And.OnlyContain(row => row.Quantity == 1 && row.Revision == 1);
        var removed = await writes.RemoveAsync(new("item", 0, suffix, 2));
        removed.Items.Should().BeEquivalentTo(changed.Items);
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await writes.RemoveAsync(new("item", 0, suffix, 2))).Items.Should().BeEmpty();
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ShadowPredicatesCanCompareComputedValuesWithoutPretendingTheyAreTrackedVersions()
    {
        await using var fixture = await CreateAsync();
        var operations = Operations(Compile(fixture.Context), fixture.Context);
        var changed = await Call<Task<MutationResult<int>>>(operations, "CompareAsync", 11, CancellationToken.None);
        changed.Items.Should().Equal(12, 12);
        (await Call<Task<MutationResult<int>>>(operations, "CompareAsync", 11, CancellationToken.None)).AffectedRows.Should().Be(0);
        (await Call<Task<MutationResult<int>>>(operations, "CompareAsync", 12, CancellationToken.None)).Items.Should().Equal(13, 13);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task NullableScalarResultsAreNotDroppedOrChangedToDefaults()
    {
        await using var fixture = await CreateAsync();
        var result = await Call<Task<MutationResult<string?>>>(Operations(Compile(fixture.Context), fixture.Context), "ClearNotesAsync", CancellationToken.None);
        result.Items.Should().BeEquivalentTo(new string?[] { null, "original" });
        result.AffectedRows.Should().Be(2);
    }

    [Fact]
    public async Task ReturningShadowValuesDoesNotSaveOrSynchronizeTheChangeTracker()
    {
        await using var fixture = await CreateAsync();
        var tracked = await fixture.Context.Rows.SingleAsync(row => row.Id == 1 && row.TenantId == 1);
        fixture.Context.Entry(tracked).Property("OptionalNote").CurrentValue = "pending shadow edit";
        var writes = (IReturningShadowWrites)Operations(Compile(fixture.Context), fixture.Context);
        var result = await writes.AdjustAsync(new("item", 3, "!", 1));
        result.Items.Single(row => row.Id == 1).OptionalNote.Should().BeNull();
        tracked.Text.Should().Be("item1"); tracked.Quantity.Should().Be(1); tracked.Revision.Should().Be(1);
        fixture.Context.Entry(tracked).Property("OptionalNote").CurrentValue.Should().Be("pending shadow edit");
        fixture.Context.Entry(tracked).Property("ComputedVersion").CurrentValue.Should().Be(11);
        fixture.Context.ChangeTracker.Entries().Should().ContainSingle();
        (await fixture.Context.Rows.AsNoTracking().Where(row => row.Id == 1 && row.TenantId == 1)
            .Select(row => EF.Property<string?>(row, "OptionalNote")).SingleAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData("projection")]
    [InlineData("constraint")]
    [InlineData("materialization")]
    public async Task FailuresRestoreTheDatabaseAndNeverPublishSuccessfulChanges(string failure)
    {
        await using var fixture = await CreateAsync();
        await using var alternate = Empty("lyingNull", (SqliteConnection)fixture.Context.Database.GetDbConnection());
        var context = failure == "materialization" ? alternate : fixture.Context;
        var observer = new ReturningShadowObserver();
        await using var work = new RepositoryUnitOfWork<ReturningShadowContext>(context, [observer]);
        var operations = Operations(Compile(context, Builder(leases: true)), work);
        Func<Task> action = async () =>
        {
            if (failure == "projection") await Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
            else await ((IReturningShadowWrites)operations).AdjustAsync(new("item", failure == "constraint" ? -30 : 2, "changed", 1));
        };
        var error = (await action.Should().ThrowAsync<Exception>()).Which;
        if (failure == "constraint") error.Should().BeOfType<SqliteException>();
        else error.Should().BeOfType<InvalidOperationException>();
        if (failure == "projection") error.Message.Should().StartWith("Shadow result failure:");
        if (failure == "materialization") error.Message.Should().Contain("non-nullable returned column was null: OptionalNote");
        (await fixture.Context.Rows.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1 && row.Revision == 1);
        observer.Calls.Should().Be(0); context.Database.CurrentTransaction.Should().BeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedTransactionsKeepSavepointsAndNotifyOnlyAfterCommit(bool rollback)
    {
        await using var fixture = await CreateAsync();
        var observer = new ReturningShadowObserver();
        await using var work = new RepositoryUnitOfWork<ReturningShadowContext>(fixture.Context, [observer]);
        var operations = Operations(Compile(fixture.Context, Builder(leases: true)), work);
        await using var transaction = await work.BeginTransactionAsync();
        (await ((IReturningShadowWrites)operations).AdjustAsync(new("item", 2, "!", 1))).AffectedRows.Should().Be(2);
        Func<Task> failed = async () => await Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Context.Rows.AsNoTracking().Where(row => row.TenantId == 1).Select(row => EF.Property<int>(row, "ComputedVersion")).ToListAsync())
            .Should().BeEquivalentTo<int>([19, 21]);
        observer.Calls.Should().Be(0);
        if (rollback)
        {
            (await ((IReturningShadowWrites)operations).RemoveAsync(new("item", 0, "", 2))).AffectedRows.Should().Be(2);
            await transaction.RollbackAsync(); observer.Calls.Should().Be(0);
            (await fixture.Context.Rows.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Should().HaveCount(4).And.OnlyContain(row => row.Quantity == 1);
        }
        else
        {
            await transaction.CommitAsync(); observer.Calls.Should().Be(1);
            observer.Tags.Should().Contain(tag => tag.EndsWith(":" + typeof(ReturningShadowRow).FullName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task CancellationAndNoMatchDoNotNotifyOrMutate()
    {
        await using var fixture = await CreateAsync(); var observer = new ReturningShadowObserver();
        await using var work = new RepositoryUnitOfWork<ReturningShadowContext>(fixture.Context, [observer]);
        var writes = (IReturningShadowWrites)Operations(Compile(fixture.Context, Builder(leases: true)), work);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Func<Task> action = async () => await writes.AdjustAsync(new("item", 1, "!", 1), canceled.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await writes.AdjustAsync(new("item", 1, "!", 99))).AffectedRows.Should().Be(0);
        (await writes.RemoveAsync(new("missing", 0, "", 1))).AffectedRows.Should().Be(0);
        observer.Calls.Should().Be(0); fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        (await fixture.Context.Rows.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
    }

    [Theory]
    [InlineData("Credit")]
    [InlineData("ComputedVersion")]
    [InlineData("Id")]
    public async Task TheStateWriterRejectsShadowGeneratedAndKeyAssignmentTargetsBeforeDml(string property)
    {
        await using var fixture = await CreateAsync();
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        var source = fixture.Context.Rows.Select(row => new { __repoGenKey0 = row.TenantId, __repoGenKey1 = row.Id, __repoGenValue0 = 3 });
        Func<Task> action = async () => await new RonSijm.RepoGen.Sqlite.SqliteReturningWriter().ExecuteWithStateAsync<ReturningShadowRow>(
            fixture.Context, source, ReturningWriteKind.Update, [new(property, "__repoGenValue0")]);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot assign*");
        (await fixture.Context.Rows.IgnoreQueryFilters().AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
