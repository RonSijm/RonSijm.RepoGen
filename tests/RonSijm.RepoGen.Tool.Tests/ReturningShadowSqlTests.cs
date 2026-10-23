using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowSqlTests
{
    [SqlServerFixtureFact]
    public async Task OutputReturnsComputedShadowValuesAndNewRowversionsThenPredeleteValues()
    {
        await using var fixture = await ReturningShadowSqlFixture.CreateAsync(); await using var context = fixture.Context();
        await Seed(context);
        var builder = ReturningShadowTestHarness.Builder().Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        var operations = ReturningShadowTestHarness.Operations(ImmediateTestHarness.CompileProduction(context, builder), context, new SqlServerReturningWriter());
        var before = await context.Rows.AsNoTracking().Where(row => row.TenantId == 1).ToDictionaryAsync(row => row.Id, row => EF.Property<byte[]>(row, "Fingerprint"));
        var writes = (IReturningShadowWrites)operations;
        var result = await writes.AdjustAsync(new("item", 2, "'; --", 1));
        result.AffectedRows.Should().Be(2);
        foreach (var row in result.Items)
        {
            row.Normalized.Should().Be(row.Text.ToUpperInvariant()); row.ComputedVersion.Should().Be(row.Quantity + 10);
            row.Quantity.Should().Be(row.Credit + 2); row.Fingerprint.Should().HaveCount(8).And.NotEqual(before[row.Id]);
            row.Status.Should().Be(ReturningState.Ready); row.OptionalNote.Should().BeNull();
        }
        var removed = await writes.RemoveAsync(new("item", 0, "'; --", 2));
        removed.Items.Should().BeEquivalentTo(result.Items);
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(2); context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task OutputShadowMappingFailureRestoresItsSavepointAndCommitObserversStayDeferred()
    {
        await using var fixture = await ReturningShadowSqlFixture.CreateAsync(); await using var context = fixture.Context(); await Seed(context);
        var builder = ReturningShadowTestHarness.Builder(leases: true).Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        var observer = new ReturningShadowObserver(); await using var work = new RepositoryUnitOfWork<ReturningShadowSqlContext>(context, [observer]);
        var operations = ReturningShadowTestHarness.Operations(ImmediateTestHarness.CompileProduction(context, builder), work, new SqlServerReturningWriter());
        await using var transaction = await work.BeginTransactionAsync();
        await ((IReturningShadowWrites)operations).AdjustAsync(new("item", 2, "!", 1));
        Func<Task> failed = async () => await ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>().WithMessage("Shadow result failure:*");
        (await context.Rows.AsNoTracking().Where(row => row.TenantId == 1).Select(row => EF.Property<int>(row, "ComputedVersion")).ToListAsync()).Should().Equal(19, 19);
        observer.Calls.Should().Be(0); await transaction.CommitAsync(); observer.Calls.Should().Be(1);
    }

    private static async Task Seed(ReturningShadowSqlContext context)
    {
        foreach (var (tenant, id, scope) in new[] { (1, 1, "alpha"), (1, 2, "alpha"), (1, 3, "beta"), (2, 1, "alpha") })
        {
            var row = new ReturningShadowRow { TenantId = tenant, Id = id, Text = "item" + id, Quantity = 1 };
            context.Add(row); context.Entry(row).Property("Scope").CurrentValue = scope;
            context.Entry(row).Property("Status").CurrentValue = ReturningState.Ready;
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
}
