using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnShadowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShadowOnlyCaptureUsesIndependentMappedModelFilterAndNeverRefiltersReturnedValues(bool delete, bool broad)
    {
        await using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(delete, broad);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Shadow.Testing.MappedReturnShadowContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MappedReturnShadowSeed, int>)data.GetType().GetProperty("MappedReturnShadowRowStore")!.GetValue(data)!;
        store.SeedRange([new(1, 2, 3), new(2, 4, 5)]);
        var fake = Activator.CreateInstance(assembly.GetType("Generated.Shadow.Testing.MappedReturnShadowRowRepositoryTestDouble")!, fixture)!;
        var actual = await MappedStateTestHarness.Call<MutationResult<int>>(fake, "ChangeAsync");
        actual.Items.Should().Equal(delete ? 3 : 6);
        store.Find(2)!.Revision.Should().Be(5);
        if (delete)
        {
            store.Find(1).Should().BeNull();
        }
        else
        {
            store.Find(1)!.Revision.Should().Be(6);
        }
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MappedReturnShadowRow { Id = 1, Amount = 2 }, new MappedReturnShadowRow { Id = 2, Amount = 4 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var production = ImmediateTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType("Generated.Shadow.Persistence.MappedReturnShadowRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var expected = await MappedStateTestHarness.Call<MutationResult<int>>(real, "ChangeAsync");
        actual.Items.Should().Equal(expected.Items);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("returningPropertyState")
            .And.Contain(delete ? "MatchedPreDeleteValuesNoPostMutationFilter" : "MatchedPostUpdateValuesNoPostMutationFilter",
                "returned values come from matched rows, not a fresh filtered read");
    }

    private static RepositoryModelBuilder Builder(bool delete, bool broad)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.Shadow.Contracts").UseGeneratedNamespace("Generated.Shadow.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseScriptedTestDoubles("Generated.Shadow.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MappedReturnShadowRow>();
        entity.HasInMemoryStore<MappedReturnShadowSeed, int>(row => row.Id, row => new(row.Id, row.Amount, row.Revision))
            .MapInMemoryEntity<MappedReturnShadowSeed>(row => new MappedReturnShadowRow { Id = row.Id, Amount = row.Amount })
            .MapInMemoryProperty<MappedReturnShadowSeed, int>("Revision", row => row.Revision)
            .MapInMemoryWrites<MappedReturnShadowSeed>((row, saved) => new(row.Id, row.Amount, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Amount + 1);
        var command = entity.HasCommand("Change");
        if (delete)
        {
            command.Delete();
        }
        else
        {
            command.Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 3));
        }
        command.ReturnsMany(row => EF.Property<int>(row, "Revision")).IncludeShadowProperties(!broad).IncludeMappedProperties(broad)
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        return builder;
    }
}
