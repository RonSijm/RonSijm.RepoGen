using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicSqlTests
{
    [SqlServerFixtureFact]
    public async Task OutputAndUpdatedQueryReturnMappedValuesWithOriginalVersionsAndRetainedStaging()
    {
        await using var fixture = await ReturningMappedSqlFixture.CreateAsync(); await using var context = fixture.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.AtomicMappedSql.Contracts").UseGeneratedNamespace("Generated.AtomicMappedSql.Persistence")
            .UseAtomicWrites<SqlServerAtomicWriter>());
        var entity = builder.Entity<ReturningMappedRow>();
        foreach (var name in new[] { "Ensure", "Put" })
        {
            var command = entity.HasCommand(name).Create<ReturningMappedRequest>(request => new ReturningMappedRow
                    { Id = request.Id, Title = request.Title, Quantity = 1 }).AllowWrite(row => row.Id);
            if (name == "Ensure") command.InsertIfAbsent(row => row.Id);
            else command.InsertOrUpdate(row => row.Id, setters => setters
                    .SetProperty<string, ReturningMappedRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Quantity, row => row.Quantity + 1))
                .ExpectVersion<int, ReturningMappedRequest>("audit", request => request.Version);
            command.Returns(row => new ReturningMappedView(row.Id, row.Title, row.Caption, EF.Property<int>(row, "audit"),
                EF.Property<ReturningState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), EF.Property<string?>(row, ReturningMappedContext.Note)))
                .IncludeMappedProperties();
        }
        var assembly = ImmediateTestHarness.CompileProduction(context, builder);
        var repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicMappedSql.Persistence.ReturningMappedRowRepositoryOperations")!, context, new SqlServerAtomicWriter())!;
        var staged = new ReturningMappedRow { Id = 2, Title = "pending" }; context.Add(staged);
        var request = new ReturningMappedRequest(Title: "native ' quoted");
        var inserted = await ReturningWriteTests.Call<Task<CommandResult<ReturningMappedView>>>(repository, "EnsureAsync", request, CancellationToken.None);
        inserted.Status.Should().Be(CommandStatus.Inserted);
        inserted.Value!.Caption.Should().Be("NATIVE ' QUOTED"); inserted.Value.Audit.Should().Be(11);
        inserted.Value.State.Should().BeNull(); inserted.Value.Fingerprint.Should().BeNull();
        var updated = await ReturningWriteTests.Call<Task<CommandResult<ReturningMappedView>>>(repository, "PutAsync", request with { Title = "updated" }, CancellationToken.None);
        updated.Status.Should().Be(CommandStatus.Updated);
        updated.Value!.Caption.Should().Be("UPDATED"); updated.Value.Audit.Should().Be(12);
        var stale = await ReturningWriteTests.Call<Task<CommandResult<ReturningMappedView>>>(repository, "PutAsync", request, CancellationToken.None);
        stale.Status.Should().Be(CommandStatus.NotMatched); stale.HasValue.Should().BeFalse();
        context.ChangeTracker.Entries().Should().ContainSingle().Which.Entity.Should().BeSameAs(staged);
        context.Entry(staged).State.Should().Be(EntityState.Added);
        (await context.Rows.CountAsync()).Should().Be(1);
    }

    [SqlServerFixtureFact]
    public async Task FailingMappedMaterializationRollsBackOwnedInsertWithoutTrackerState()
    {
        await using var fixture = await ReturningMappedSqlFixture.CreateAsync(); await using var context = fixture.Context();
        ReturningMappedRow.FailIndexerAssignment.Value = true;
        try
        {
            Func<Task> failure = () => AtomicWriteExecutor.ExecuteWithMappedStateAsync<ReturningMappedRow, int>(context, new SqlServerAtomicWriter(),
                () => new ReturningMappedRow { Id = 1, Title = "new", Quantity = 1 }, ["Id", "Title", "Quantity"], ["Id"], null,
                row => row.GetValue<int>("audit"), null, []);
            await failure.Should().ThrowAsync<Exception>();
        }
        finally { ReturningMappedRow.FailIndexerAssignment.Value = false; }
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
