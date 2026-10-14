using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryTranslationContractTests
{
    [Theory]
    [InlineData(false, "%AP%")]
    [InlineData(true, "%AP%")]
    [InlineData(false, "_ear")]
    [InlineData(true, "_ear")]
    [InlineData(false, "a%")]
    [InlineData(true, "a%")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    public async Task TheSameDesignerExecutesPatternAndRegisteredLengthCallsOnFixturesAndRealSqlite(bool mapped, string pattern)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new TranslatedReadContext(new DbContextOptionsBuilder<TranslatedReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(InMemoryQueryTestHarness.Seeds()); await context.SaveChangesAsync();
        var builder = InMemoryQueryTestHarness.Builder(strict: true); InMemoryTranslationTestHarness.Register(builder);
        var entity = builder.Entity<PortableRow>(); entity.WriteEnabled = false;
        if (mapped) entity.HasInMemoryStore<TranslationSeed, int>(row => row.Id, row => new TranslationSeed(row.Id, row.Name, row.Category, row.Price, row.Hidden))
            .MapInMemoryEntity<TranslationSeed>(row => new PortableRow { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden });
        else entity.HasInMemoryStore(row => row.Id, row => new PortableRow { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        entity.HasQuery("Sizes").Where<string>((row, input) => EF.Functions.Like(row.Name, input))
            .OrderBy(row => row.Id).Select(row => TranslationCalls.Measure(row.Name)).List();
        var design = builder.Build();
        var assembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(assembly.GetType("Generated.Portable.Persistence.PortableRowRepositoryOperations")!, context)!;
        var fixtureAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var fixture = Activator.CreateInstance(fixtureAssembly.GetType("Generated.Portable.TestDoubles.TranslatedReadContextRepositoryFixture")!)!;
        var fake = Activator.CreateInstance(fixtureAssembly.GetType("Generated.Portable.TestDoubles.PortableRowRepositoryTestDouble")!, fixture)!;
        var data = fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = data.GetType().GetProperty("PortableRowStore")!.GetValue(data)!;
        if (mapped) ((InMemoryStore<TranslationSeed, int>)store).SeedRange(InMemoryQueryTestHarness.Seeds().Select(row => new TranslationSeed(row.Id, row.Name, row.Category, row.Price, row.Hidden)));
        else ((InMemoryStore<PortableRow, int>)store).SeedRange(InMemoryQueryTestHarness.Seeds());
        (await InMemoryQueryTestHarness.Invoke<Task<IReadOnlyList<int>>>(fake, "SizesAsync", pattern))
            .Should().Equal(await InMemoryQueryTestHarness.Invoke<Task<IReadOnlyList<int>>>(real, "SizesAsync", pattern));
    }
}
