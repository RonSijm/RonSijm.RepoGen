using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappedCaptureComposesWithFreshQueriesNotNativeBufferProjection(bool fixture)
    {
        await using var context = MappedAtomicTestHarness.Context();
        var builder = MappedAtomicTestHarness.Builder();
        var entity = builder.Entity<MappedStateNote>();
        entity.HasCommand("Fresh").Create<MappedAtomicRequest>(request => new MappedStateNote
                { Id = request.Id, Title = request.Title, Counter = request.Delta }).AllowWrite(row => row.Id)
            .InsertOrUpdate(row => row.Title, setters => setters.SetProperty<int, MappedAtomicRequest>(row => row.Counter, (row, request) => row.Counter + request.Delta))
            .ExpectVersion<int, MappedAtomicRequest>("Revision", request => request.Revision)
            .ReturnsSaved(MappedAtomicTestHarness.Projection).IncludeMappedProperties()
            .UseInMemoryModelQueryFilter().SimulateImmediateWrites();
        object repository;
        if (fixture)
        {
            var assembly = MappedAtomicTestHarness.Compile(builder);
            (repository, _, _) = MappedAtomicTestHarness.Fixture(assembly);
        }
        else
        {
            await MappedAtomicTestHarness.SeedAsync(context);
            var assembly = ImmediateTestHarness.CompileProduction(context, builder);
            repository = Activator.CreateInstance(assembly.GetType(MappedAtomicTestHarness.Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        }
        var inserted = await MappedAtomicTestHarness.Call<CommandResult<MappedStateCard>>(repository, "Fresh", new(20, "new", 2));
        inserted.Value.Should().BeEquivalentTo(new MappedStateCard(20, "new", 2, "NEW", 12, 3, 1, [1, 2]));
        var updated = await MappedAtomicTestHarness.Call<CommandResult<MappedStateCard>>(repository, "Fresh", new(900, "one", 2));
        updated.Value!.Id.Should().Be(1);
        updated.Value.Revision.Should().Be(5);
        updated.Value.Caption.Should().Be("ONE");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShadowOnlyCapabilityAndFreshRootModelFilterRemainIndependent(bool fixture)
    {
        await using var context = new MappedReturnShadowContext(new DbContextOptionsBuilder<MappedReturnShadowContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.AtomicSavedShadow.Contracts").UseGeneratedNamespace("Generated.AtomicSavedShadow.Persistence")
            .UseAtomicWrites<AtomicShadowOnlyWriter>().UseScriptedTestDoubles("Generated.AtomicSavedShadow.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MappedReturnShadowRow>();
        entity.HasInMemoryStore<MappedReturnShadowSeed, int>(row => row.Id, row => new(row.Id, row.Amount, row.Revision))
            .MapInMemoryEntity<MappedReturnShadowSeed>(row => new MappedReturnShadowRow { Id = row.Id, Amount = row.Amount })
            .MapInMemoryProperty<MappedReturnShadowSeed, int>("Revision", row => row.Revision)
            .MapInMemoryWrites<MappedReturnShadowSeed>((row, saved) => new(row.Id, row.Amount, EF.Property<int>(row, "Revision")), useMappedPropertyState: true)
            .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Amount + 1);
        entity.HasCommand("Put").Create<MappedReturnShadowSeed>(request => new MappedReturnShadowRow { Id = request.Id, Amount = request.Amount })
            .AllowWrite(row => row.Id).InsertOrUpdate(row => row.Id, setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1))
            .ExpectVersion<int, MappedReturnShadowSeed>("Revision", request => request.Revision)
            .ReturnsSaved(row => EF.Property<int>(row, "Revision")).IncludeShadowProperties()
            .UseInMemoryModelQueryFilter().SimulateImmediateWrites();
        object repository;
        if (fixture)
        {
            var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
            var data = Activator.CreateInstance(assembly.GetType("Generated.AtomicSavedShadow.Testing.MappedReturnShadowContextRepositoryFixture")!)!;
            repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicSavedShadow.Testing.MappedReturnShadowRowRepositoryTestDouble")!, data)!;
        }
        else
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            var assembly = ImmediateTestHarness.CompileProduction(context, builder);
            repository = Activator.CreateInstance(assembly.GetType("Generated.AtomicSavedShadow.Persistence.MappedReturnShadowRowRepositoryOperations")!, context, new AtomicShadowOnlyWriter())!;
        }
        var first = await MappedStateTestHarness.Call<CommandResult<int>>(repository, "PutAsync", new MappedReturnShadowSeed(1, 1, 2));
        first.Value.Should().Be(2);
        var updated = await MappedStateTestHarness.Call<CommandResult<int>>(repository, "PutAsync", new MappedReturnShadowSeed(1, 1, 2));
        updated.Value.Should().Be(3);
        // Insertion itself is unfiltered, but the explicitly chosen fresh result must satisfy the root model filter.
        Func<Task> filtered = () => MappedStateTestHarness.Call<CommandResult<int>>(repository, "PutAsync", new MappedReturnShadowSeed(2, 10, 11));
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        if (!fixture)
        {
            (await context.Set<MappedReturnShadowRow>().IgnoreQueryFilters().CountAsync()).Should().Be(1);
        }
    }
}
