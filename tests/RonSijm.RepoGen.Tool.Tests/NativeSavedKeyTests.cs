using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedKeyTests
{
    [Fact]
    public async Task ActualSqliteAndMappedFixturesCorrelateEveryCompositeKeyComponent()
    {
        await using var context = new MemoryKeyContext(new DbContextOptionsBuilder<MemoryKeyContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryKeyCompoundRow { Code = "same", Slot = 1, Title = "first" }, new MemoryKeyCompoundRow { Code = "same", Slot = 2, Title = "second" },
            new MemoryKeyCompoundRow { Code = "other", Slot = 1, Title = "other" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.NativeSavedKeys.Contracts").UseGeneratedNamespace("Generated.NativeSavedKeys.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseScriptedTestDoubles("Generated.NativeSavedKeys.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MemoryKeyCompoundRow>();
        entity.HasInMemoryStore<MemoryKeyCompoundData, (string, int)>(row => new(row.Code, row.Slot), row => new(row.Code, row.Slot, row.Title))
            .MapInMemoryEntity<MemoryKeyCompoundData>(row => new MemoryKeyCompoundRow { Code = row.Code, Slot = row.Slot, Title = row.Title })
            .MapInMemoryWrites<MemoryKeyCompoundData>((row, saved) => new(row.Code, row.Slot, row.Title));
        entity.HasCommand("Rewrite").Where<string>((row, code) => row.Code == code)
            .Update(setters => setters.SetProperty(row => row.Title, "changed"))
            .ReturnsSavedMany(row => new MemoryKeyCompoundData(row.Code, row.Slot, row.Title), maximumRows: 2).SimulateImmediateWrites();
        var production = ImmediateTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType("Generated.NativeSavedKeys.Persistence.MemoryKeyCompoundRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var testing = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(testing.GetType("Generated.NativeSavedKeys.Testing.MemoryKeyContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MemoryKeyCompoundData, (string, int)>)data.GetType().GetProperty("MemoryKeyCompoundRowStore")!.GetValue(data)!;
        store.SeedRange([new("same", 1, "first"), new("same", 2, "second"), new("other", 1, "other")]);
        var memory = Activator.CreateInstance(testing.GetType("Generated.NativeSavedKeys.Testing.MemoryKeyCompoundRowRepositoryTestDouble")!, fixture)!;
        var expected = await MappedStateTestHarness.Call<MutationResult<MemoryKeyCompoundData>>(real, "RewriteAsync", "same");
        var actual = await MappedStateTestHarness.Call<MutationResult<MemoryKeyCompoundData>>(memory, "RewriteAsync", "same");
        actual.Items.Should().BeEquivalentTo(expected.Items).And.HaveCount(2);
        store.Find(("other", 1))!.Title.Should().Be("other");
    }
}
