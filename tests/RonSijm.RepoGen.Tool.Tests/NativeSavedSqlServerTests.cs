using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.SqlServer;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedSqlServerTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SqlServerFreshGraphOperationsCompileInEveryLayoutWithoutOpeningConnection(RepositoryOutputLayout layout)
    {
        using var context = new MemoryCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlServer("Server=unused;Database=unused").Options);
        var builder = NativeSavedTestHarness.Builder().Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        NativeSavedTestHarness.CompileProduction(context, builder, layout);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build(), layout));
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [SqlServerFixtureFact]
    public async Task SqlServerFreshMappedResultsAndOversizedRollbackLeaveTrackedWorkUntouched()
    {
        await using var fixture = await ReturningMappedSqlFixture.CreateAsync();
        await using var context = fixture.Context();
        context.AddRange(new ReturningMappedRow { Id = 1, Title = "one", Quantity = 1 }, new ReturningMappedRow { Id = 2, Title = "two", Quantity = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.NativeSavedSql.Contracts").UseGeneratedNamespace("Generated.NativeSavedSql.Persistence")
            .UseReturningWrites<SqlServerReturningWriter>());
        foreach (var name in new[] { "Rewrite", "TooMany" })
        {
            var command = builder.Entity<ReturningMappedRow>().HasCommand(name);
            if (name == "Rewrite")
            {
                command.Where<ReturningMappedRequest>((row, request) => row.Id == request.Id)
                    .ExpectVersion<int, ReturningMappedRequest>("audit", request => request.Version);
            }
            command.Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1))
                .ReturnsSavedMany(row => new ReturningMappedView(row.Id, row.Title, row.Caption, EF.Property<int>(row, "audit"),
                    EF.Property<ReturningState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), EF.Property<string?>(row, ReturningMappedContext.Note)),
                    maximumRows: name == "TooMany" ? 1 : 3).IncludeMappedProperties();
        }
        var assembly = NativeSavedTestHarness.CompileProduction(context, builder);
        var repository = Activator.CreateInstance(assembly.GetType("Generated.NativeSavedSql.Persistence.ReturningMappedRowRepositoryOperations")!, context, new SqlServerReturningWriter())!;
        context.Add(new ReturningMappedRow { Id = 99, Title = "pending" });
        Func<Task> oversized = () => ReturningWriteTests.Call<Task<MutationResult<ReturningMappedView>>>(repository, "TooManyAsync", CancellationToken.None);
        await oversized.Should().ThrowAsync<InvalidOperationException>().WithMessage("*row bound*");
        (await context.Rows.OrderBy(row => row.Id).Select(row => row.Quantity).ToListAsync()).Should().Equal(1, 1);
        var result = await ReturningWriteTests.Call<Task<MutationResult<ReturningMappedView>>>(repository, "RewriteAsync", new ReturningMappedRequest(), CancellationToken.None);
        result.Items.Should().ContainSingle().Which.Audit.Should().Be(12);
        result.Items[0].Caption.Should().Be("ONE");
        context.ChangeTracker.Entries().Should().ContainSingle().Which.State.Should().Be(EntityState.Added);
    }
}
