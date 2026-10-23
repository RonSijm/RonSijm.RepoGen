using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.SqlServer;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedSqlServerTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SqlServerSavedQueryPathCompilesWithoutOpeningConnection(RepositoryOutputLayout layout)
    {
        using var context = MappedAtomicTestHarness.Context(sqlServer: true);
        var builder = MappedAtomicTestHarness.Builder(sqlServer: true);
        builder.Entity<MappedStateNote>().HasCommand("Fresh")
            .Create<MappedAtomicRequest>(request => new MappedStateNote { Id = request.Id, Title = request.Title, Counter = request.Delta })
            .AllowWrite(row => row.Id).InsertIfAbsent(row => row.Title).ReturnsSaved(MappedAtomicTestHarness.Projection)
            .IncludeMappedProperties().UseInMemoryModelQueryFilter().SimulateImmediateWrites();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        ImmediateTestHarness.CompileProduction(context, builder);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [SqlServerFixtureFact]
    public async Task FreshMappedSqlQueriesUseSavedIdentityAndVersionsWithoutFlushingTrackedWork()
    {
        await using var fixture = await ReturningMappedSqlFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.AtomicSavedSql.Contracts").UseGeneratedNamespace("Generated.AtomicSavedSql.Persistence")
            .UseAtomicWrites<SqlServerAtomicWriter>());
        builder.Entity<ReturningMappedRow>().HasCommand("Put")
            .Create<ReturningMappedRequest>(request => new ReturningMappedRow { Id = request.Id, Title = request.Title, Quantity = 1 })
            .AllowWrite(row => row.Id).InsertOrUpdate(row => row.Id, setters => setters
                .SetProperty<string, ReturningMappedRequest>(row => row.Title, (row, request) => request.Title)
                .SetProperty(row => row.Quantity, row => row.Quantity + 1))
            .ExpectVersion<int, ReturningMappedRequest>("audit", request => request.Version)
            .ReturnsSaved(row => new ReturningMappedView(row.Id, row.Title, row.Caption, EF.Property<int>(row, "audit"),
                EF.Property<ReturningState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), EF.Property<string?>(row, ReturningMappedContext.Note)))
            .IncludeMappedProperties();
        var assembly = ImmediateTestHarness.CompileProduction(context, builder);
        var repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicSavedSql.Persistence.ReturningMappedRowRepositoryOperations")!, context, new SqlServerAtomicWriter())!;
        context.Add(new ReturningMappedRow { Id = 99, Title = "pending" });
        var request = new ReturningMappedRequest(Title: "fresh");
        var inserted = await ReturningWriteTests.Call<Task<CommandResult<ReturningMappedView>>>(repository, "PutAsync", request, CancellationToken.None);
        inserted.Value!.Caption.Should().Be("FRESH");
        inserted.Value.Audit.Should().Be(11);
        var updated = await ReturningWriteTests.Call<Task<CommandResult<ReturningMappedView>>>(repository, "PutAsync", request with { Title = "updated" }, CancellationToken.None);
        updated.Value!.Caption.Should().Be("UPDATED");
        updated.Value.Audit.Should().Be(12);
        context.ChangeTracker.Entries().Should().ContainSingle().Which.State.Should().Be(EntityState.Added);
        (await context.Rows.CountAsync()).Should().Be(1);
    }
}
