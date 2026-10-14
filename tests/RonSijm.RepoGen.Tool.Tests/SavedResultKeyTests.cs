using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultKeyTests
{
    [Fact]
    public async Task CompleteCompositeKeySelectsTheSavedRowInActualSqliteAndMappedFixtures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryKeyContext(new DbContextOptionsBuilder<MemoryKeyContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryKeyCompoundRow { Code = "same", Slot = 1, Title = "first" }, new MemoryKeyCompoundRow { Code = "same", Slot = 2, Title = "second" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.SavedResults.Contracts")
            .UseGeneratedNamespace("Generated.SavedResults.Persistence").UseScriptedTestDoubles("Generated.SavedResults.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<MemoryKeyCompoundRow>();
        entity.HasInMemoryStore<MemoryKeyCompoundData, (string, int)>(row => new ValueTuple<string, int>(row.Code, row.Slot), row => new(row.Code, row.Slot, row.Title))
            .MapInMemoryEntity<MemoryKeyCompoundData>(row => new MemoryKeyCompoundRow { Code = row.Code, Slot = row.Slot, Title = row.Title })
            .MapInMemoryWrites<MemoryKeyCompoundData>((row, saved) => new(row.Code, row.Slot, row.Title));
        entity.HasCommand("Rename").Where<int>((row, slot) => row.Slot == slot).Patch(setters => setters.SetProperty(row => row.Title, "saved"))
            .Save().ReturnsSaved(row => new MemoryKeyCompoundData(row.Code, row.Slot, row.Title));
        var design = builder.Build();
        var production = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(production.GetType("Generated.SavedResults.Persistence.MemoryKeyCompoundRowRepositoryOperations")!, context)!;
        var testing = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var fixture = Activator.CreateInstance(testing.GetType("Generated.SavedResults.TestDoubles.MemoryKeyContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MemoryKeyCompoundData, (string, int)>)data.GetType().GetProperty("MemoryKeyCompoundRowStore")!.GetValue(data)!;
        store.SeedRange([new("same", 1, "first"), new("same", 2, "second")]);
        var fake = Activator.CreateInstance(testing.GetType("Generated.SavedResults.TestDoubles.MemoryKeyCompoundRowRepositoryTestDouble")!, fixture)!;
        (await Invoke<CommandResult<MemoryKeyCompoundData>>(fake, "RenameAsync", 2)).Should().BeEquivalentTo(await Invoke<CommandResult<MemoryKeyCompoundData>>(real, "RenameAsync", 2));
        store.Snapshot().Single(row => row.Slot == 1).Title.Should().Be("first");
    }
}
