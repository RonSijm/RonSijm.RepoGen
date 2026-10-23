using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicShadowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowOnlyCaptureWorksOnBothBranchesWithIndependentFixtureConsent(bool fixture)
    {
        await using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder();
        object repository;
        if (fixture)
        {
            var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
            var data = Activator.CreateInstance(assembly.GetType("Generated.AtomicShadow.Testing.MappedReturnShadowContextRepositoryFixture")!)!;
            repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicShadow.Testing.MappedReturnShadowRowRepositoryTestDouble")!, data)!;
        }
        else
        {
            await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
            var assembly = ImmediateTestHarness.CompileProduction(context, builder);
            repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicShadow.Persistence.MappedReturnShadowRowRepositoryOperations")!, context, new AtomicShadowOnlyWriter())!;
        }
        var request = new MappedReturnShadowSeed(1, 1, 2);
        var inserted = await MappedStateTestHarness.Call<CommandResult<int>>(repository, "EnsureAsync", request);
        inserted.Status.Should().Be(CommandStatus.Inserted); inserted.Value.Should().Be(2);
        var updated = await MappedStateTestHarness.Call<CommandResult<int>>(repository, "PutAsync", request);
        updated.Status.Should().Be(CommandStatus.Updated); updated.Value.Should().Be(3);
        var stale = await MappedStateTestHarness.Call<CommandResult<int>>(repository, "PutAsync", request);
        stale.Status.Should().Be(CommandStatus.NotMatched); stale.HasValue.Should().BeFalse();
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void APlainAtomicWriterCannotClaimShadowCapture()
    {
        using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder().Configure(options => options.UseAtomicWrites<TimeoutAtomicWriter>());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1307:*IRepositoryAtomicStateWriter*");
    }

    [Theory]
    [InlineData("noResult")]
    [InlineData("wrongType")]
    [InlineData("unknown")]
    [InlineData("receiver")]
    [InlineData("entity")]
    public void AtomicCaptureRequiresAConfiguredScalarProjectionWithExactReads(string shape)
    {
        using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(); var entity = builder.Entity<MappedReturnShadowRow>();
        var command = entity.HasCommand("Invalid").Create<int>(id => new MappedReturnShadowRow { Id = id, Amount = 0 })
            .AllowWrite(row => row.Id).InsertIfAbsent(row => row.Id).IncludeShadowProperties();
        switch (shape)
        {
            case "wrongType": command.Returns(row => EF.Property<long>(row, "Revision")); break;
            case "unknown": command.Returns(row => EF.Property<int>(row, "Unknown")); break;
            case "receiver": command.Returns(row => EF.Property<int>(new MappedReturnShadowRow(), "Revision")); break;
            case "entity": command.Returns(row => row); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.AtomicShadow.Contracts").UseGeneratedNamespace("Generated.AtomicShadow.Persistence")
            .UseAtomicWrites<AtomicShadowOnlyWriter>().UseScriptedTestDoubles("Generated.AtomicShadow.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MappedReturnShadowRow>();
        entity.HasInMemoryStore<MappedReturnShadowSeed, int>(row => row.Id, row => new(row.Id, row.Amount, row.Revision))
            .MapInMemoryEntity<MappedReturnShadowSeed>(row => new MappedReturnShadowRow { Id = row.Id, Amount = row.Amount })
            .MapInMemoryProperty<MappedReturnShadowSeed, int>("Revision", row => row.Revision)
            .MapInMemoryWrites<MappedReturnShadowSeed>((row, saved) => new(row.Id, row.Amount, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Amount + 1);
        foreach (var name in new[] { "Ensure", "Put" })
        {
            var command = entity.HasCommand(name).Create<MappedReturnShadowSeed>(request => new MappedReturnShadowRow { Id = request.Id, Amount = request.Amount })
                .AllowWrite(row => row.Id);
            if (name == "Ensure") command.InsertIfAbsent(row => row.Id);
            else command.InsertOrUpdate(row => row.Id, setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1))
                    .ExpectVersion<int, MappedReturnShadowSeed>("Revision", request => request.Revision);
            command.Returns(row => EF.Property<int>(row, "Revision"))
                .IncludeShadowProperties().IncludeMappedProperties().IncludeMappedProperties(false)
                .UseInMemoryModelQueryFilter().SimulateImmediateWrites();
        }
        return builder;
    }
}
