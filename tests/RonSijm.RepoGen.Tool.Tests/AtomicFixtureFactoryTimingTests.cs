using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureFactoryTimingTests
{
    [Fact]
    public void UpdateOnlyFactoriesCannotSilentlySupplyAnOmittedInsertColumn()
    {
        using var context = new AtomicUpdateOnlyContext(new DbContextOptionsBuilder<AtomicUpdateOnlyContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.AtomicTiming.Contracts").UseAtomicWrites<SqliteAtomicWriter>()
            .UseScriptedTestDoubles("Generated.AtomicTiming.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = ImmediateTestHarness.Store(builder, mapped: true);
        entity.HasInMemoryGeneratedValue(row => row.Id, (row, rows) => 5);
        entity.HasInMemoryGeneratedValue(row => row.Previous, (row, rows) => row.Amount);
        entity.HasCommand("Ensure").Create<AtomicFixtureInput>(request => new ImmediateRow { OwnerId = request.OwnerId, Code = request.Code,
                Amount = request.Amount, Version = 1, Stamp = new byte[] { 1 }, Hidden = false })
            .AllowWrite(row => row.Version).AllowWrite(row => row.Stamp).InsertIfAbsent(row => new { row.OwnerId, row.Code }).SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*explicitly assign scalar 'Previous'*");
    }
}
