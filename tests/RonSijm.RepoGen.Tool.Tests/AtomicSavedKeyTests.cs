using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedKeyTests
{
    [Fact]
    public async Task EveryCompositePrimaryKeyComponentSelectsTheFreshSavedRow()
    {
        await using var context = new MemoryKeyContext(new DbContextOptionsBuilder<MemoryKeyContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryKeyCompoundRow { Code = "same", Slot = 1, Title = "first" }, new MemoryKeyCompoundRow { Code = "same", Slot = 2, Title = "second" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.AtomicSavedKeys.Contracts").UseGeneratedNamespace("Generated.AtomicSavedKeys.Persistence")
            .UseAtomicWrites<SqliteAtomicWriter>().UseScriptedTestDoubles("Generated.AtomicSavedKeys.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MemoryKeyCompoundRow>();
        entity.HasInMemoryStore<MemoryKeyCompoundData, (string, int)>(row => new(row.Code, row.Slot), row => new(row.Code, row.Slot, row.Title))
            .MapInMemoryEntity<MemoryKeyCompoundData>(row => new MemoryKeyCompoundRow { Code = row.Code, Slot = row.Slot, Title = row.Title })
            .MapInMemoryWrites<MemoryKeyCompoundData>((row, saved) => new(row.Code, row.Slot, row.Title));
        entity.HasCommand("Put").Create<MemoryKeyCompoundData>(request => new MemoryKeyCompoundRow { Code = request.Code, Slot = request.Slot, Title = request.Title })
            .AllowWrite(row => row.Code).AllowWrite(row => row.Slot)
            .InsertOrUpdate(row => new { row.Code, row.Slot }, setters => setters.SetProperty<string, MemoryKeyCompoundData>(row => row.Title, (row, request) => request.Title))
            .ReturnsSaved(row => new MemoryKeyCompoundData(row.Code, row.Slot, row.Title)).SimulateImmediateWrites();
        var production = ImmediateTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType("Generated.AtomicSavedKeys.Persistence.MemoryKeyCompoundRowRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var testing = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(testing.GetType("Generated.AtomicSavedKeys.Testing.MemoryKeyContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MemoryKeyCompoundData, (string, int)>)data.GetType().GetProperty("MemoryKeyCompoundRowStore")!.GetValue(data)!;
        store.SeedRange([new("same", 1, "first"), new("same", 2, "second")]);
        var memory = Activator.CreateInstance(testing.GetType("Generated.AtomicSavedKeys.Testing.MemoryKeyCompoundRowRepositoryTestDouble")!, fixture)!;
        var input = new MemoryKeyCompoundData("same", 2, "changed");
        var expected = await MappedStateTestHarness.Call<CommandResult<MemoryKeyCompoundData>>(real, "PutAsync", input);
        var actual = await MappedStateTestHarness.Call<CommandResult<MemoryKeyCompoundData>>(memory, "PutAsync", input);
        actual.Should().BeEquivalentTo(expected);
        actual.Value.Should().Be(input);
        store.Find(("same", 1))!.Title.Should().Be("first");
    }
}
