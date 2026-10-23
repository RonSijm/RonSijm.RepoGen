using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedCaptureTests
{
    private static readonly int[] ExpectedShadowRevisions = [3, 4];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappedCaptureSuppliesIdentityButFreshValuesRemainSqlOrSnapshotProjections(bool fixture)
    {
        await using var context = MappedStateTestHarness.Context();
        var builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().HasCommand("Fresh")
            .Where<int>((row, id) => row.Id == id)
            .Update(setters => setters.SetProperty(row => row.Title, "fresh").SetProperty(row => row.Counter, row => row.Counter + 1))
            .ReturnsSavedMany(row => new MappedStateCard(row.Id, row.Title, row.Counter, row.Caption,
                EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"),
                EF.Property<byte[]?>(row, "Stamp")), maximumRows: 2).IncludeMappedProperties().SimulateImmediateWrites();
        object repository;
        if (fixture)
        {
            (repository, _, _) = MappedStateTestHarness.Fixture(MappedReturnTestHarness.Compile(builder));
        }
        else
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            foreach (var seed in MappedStateTestHarness.Seeds())
            {
                var row = new MappedStateNote { Id = seed.Id, Title = seed.Title, Counter = seed.Counter };
                context.Add(row);
                context.Entry(row).Property(nameof(MappedStateNote.Caption)).CurrentValue = seed.Caption;
                context.Entry(row).Property("audit").CurrentValue = seed.Audit;
            }
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var assembly = NativeSavedTestHarness.CompileProduction(context, builder);
            repository = Activator.CreateInstance(assembly.GetType(MappedStateTestHarness.Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteReturningWriter())!;
        }
        var result = await MappedStateTestHarness.Call<MutationResult<MappedStateCard>>(repository, "FreshAsync", 1);
        result.Items.Should().BeEquivalentTo(new[] { new MappedStateCard(1, "fresh", 3, "caption", 10, 4, 1, [1, 2]) });
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowOnlyCaptureRechecksTheFreshModelFilterAndNeverShortensResults(bool fixture)
    {
        await using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.NativeSavedShadow.Contracts").UseGeneratedNamespace("Generated.NativeSavedShadow.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseScriptedTestDoubles("Generated.NativeSavedShadow.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        builder.Entity<MappedReturnShadowRow>().HasInMemoryStore<MappedReturnShadowSeed, int>(row => row.Id, row => new(row.Id, row.Amount, row.Revision))
            .MapInMemoryEntity<MappedReturnShadowSeed>(row => new MappedReturnShadowRow { Id = row.Id, Amount = row.Amount })
            .MapInMemoryProperty<MappedReturnShadowSeed, int>("Revision", row => row.Revision)
            .MapInMemoryWrites<MappedReturnShadowSeed>((row, saved) => new(row.Id, row.Amount, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Amount + 1);
        builder.Entity<MappedReturnShadowRow>().HasCommand("Bump")
            .Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1))
            .ReturnsSavedMany(row => EF.Property<int>(row, "Revision"), maximumRows: 3).IncludeShadowProperties()
            .UseInMemoryModelQueryFilter().SimulateImmediateWrites();
        object repository;
        if (fixture)
        {
            var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
            var setup = Activator.CreateInstance(assembly.GetType("Generated.NativeSavedShadow.Testing.MappedReturnShadowContextRepositoryFixture")!)!;
            var data = (InMemoryWriteDataContext)setup.GetType().GetProperty("Data")!.GetValue(setup)!;
            var store = (InMemoryStore<MappedReturnShadowSeed, int>)data.GetType().GetProperty("MappedReturnShadowRowStore")!.GetValue(data)!;
            store.SeedRange([new(1, 1, 2), new(2, 2, 3)]);
            repository = Activator.CreateInstance(assembly.GetType("Generated.NativeSavedShadow.Testing.MappedReturnShadowRowRepositoryTestDouble")!, setup)!;
        }
        else
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            context.AddRange(new MappedReturnShadowRow { Id = 1, Amount = 1 }, new MappedReturnShadowRow { Id = 2, Amount = 2 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var assembly = NativeSavedTestHarness.CompileProduction(context, builder);
            repository = Activator.CreateInstance(assembly.GetType("Generated.NativeSavedShadow.Persistence.MappedReturnShadowRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        }
        var first = await MappedStateTestHarness.Call<MutationResult<int>>(repository, "BumpAsync");
        first.Items.Should().BeEquivalentTo(ExpectedShadowRevisions);
        Func<Task> filtered = () => MappedStateTestHarness.Call<MutationResult<int>>(repository, "BumpAsync");
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        if (!fixture)
        {
            (await context.Set<MappedReturnShadowRow>().IgnoreQueryFilters().OrderBy(row => row.Id).Select(row => row.Amount).ToListAsync()).Should().Equal(2, 3);
        }
    }
}
