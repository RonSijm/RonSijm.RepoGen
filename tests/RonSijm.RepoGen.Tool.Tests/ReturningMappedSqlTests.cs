using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;
using static RonSijm.RepoGen.Tool.Tests.ReturningMappedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedSqlTests
{
    [SqlServerFixtureFact]
    public async Task OutputCapturesFieldBackedIndexerAndNullableValuesWithConditionalVersions()
    {
        await using var fixture = await ReturningMappedSqlFixture.CreateAsync(); await using var context = fixture.Context();
        var row = new ReturningMappedRow { Id = 1, Title = "first" }; context.Add(row);
        context.Entry(row).Property("state").CurrentValue = ReturningState.Ready;
        row[ReturningMappedContext.Note] = "indexed"; await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = Compile(context, Builder(sqlServer: true));
        var operations = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ReturningMappedRowRepositoryOperations")!, context, new SqlServerReturningWriter())!;
        var changed = await Call(operations, "RenameAsync", new(Title: "native"));
        changed.Items.Single().Caption.Should().Be("NATIVE"); changed.Items.Single().Audit.Should().Be(12);
        changed.Items.Single().State.Should().Be(ReturningState.Ready); changed.Items.Single().Note.Should().Be("indexed");
        (await Call(operations, "RenameAsync", new())).AffectedRows.Should().Be(0);
        (await Call(operations, "RemoveAsync", new(Version: 12))).Items.Should().BeEquivalentTo(changed.Items);
        (await context.Rows.CountAsync()).Should().Be(0); context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
