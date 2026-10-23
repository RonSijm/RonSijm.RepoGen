using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningMappedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedRuntimeTests
{
    [Fact]
    public async Task MaterializationPopulatesTheConfiguredFieldAndIndexerWithoutAttachingTheEntity()
    {
        await using var fixture = await CreateAsync(); var context = fixture.Context;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var source = context.Rows.Where(row => row.Id == 1).Select(row => new { __repoGenKey0 = row.Id, __repoGenValue0 = row.Quantity + 1 });
        var result = await new SqliteReturningWriter().ExecuteWithMappedStateAsync<ReturningMappedRow>(context, source, ReturningWriteKind.Update, [new("Quantity", "__repoGenValue0")]);
        var returned = result.Single();
        returned.Entity.AuditValue().Should().Be(12); returned.Entity.StateValue().Should().Be(ReturningState.Ready);
        returned.Entity.FingerprintValue().Should().Equal(1, 2, 3);
        returned.Entity.Caption.Should().Be("getter behavior:ITEM1");
        returned.Entity[ReturningMappedContext.Note].Should().Be("original");
        returned.GetValue<string>(nameof(ReturningMappedRow.Caption)).Should().Be("ITEM1");
        context.ChangeTracker.Entries().Should().BeEmpty();
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("state")]
    [InlineData(ReturningMappedContext.Note)]
    public async Task DirectWritersDoNotAcceptFieldOrIndexerUpdateTargets(string name)
    {
        await using var fixture = await CreateAsync(); var context = fixture.Context;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var source = context.Rows.Select(row => new { __repoGenKey0 = row.Id, __repoGenValue0 = "bad" });
        Func<Task> action = () => new SqliteReturningWriter().ExecuteWithMappedStateAsync<ReturningMappedRow>(context, source, ReturningWriteKind.Update, [new(name, "__repoGenValue0")]);
        await action.Should().ThrowAsync<InvalidOperationException>();
        (await context.Rows.Select(row => row.Title).ToListAsync()).Should().BeEquivalentTo("item1", "item2");
    }
}
