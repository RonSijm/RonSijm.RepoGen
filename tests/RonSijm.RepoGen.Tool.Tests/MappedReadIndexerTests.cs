using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadIndexerTests
{
    [Fact]
    public async Task TypedStringAndBinaryIndexersWorkAsMetadataBackedScalarReadsIncludingFromRoots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MappedIndexerContext(new DbContextOptionsBuilder<MappedIndexerContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var bytes = new byte[] { 1, 2 };
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO typed_text (Id, Text) VALUES ({1}, {"raw text"})");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO typed_binary (Id, Data) VALUES ({1}, {bytes})");
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(MappedReadTestHarness.Namespace + ".Contracts").UseGeneratedNamespace(MappedReadTestHarness.Namespace + ".Persistence")
            .UseScriptedTestDoubles(MappedReadTestHarness.Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        var text = builder.Entity<MappedTextIndexerRow>();
        text.WriteEnabled = false;
        text.HasInMemoryStore<MappedIndexerSeed, int>(row => row.Id, row => new(row.Id, row.Text, row.Data.ToArray()))
            .MapInMemoryEntity<MappedIndexerSeed>(row => new MappedTextIndexerRow { Id = row.Id })
            .MapInMemoryProperty<MappedIndexerSeed, string>("Text", row => row.Text);
        text.HasQuery("Read").Select(row => row["Text"]).List();
        var binary = builder.Entity<MappedBinaryIndexerRow>();
        binary.WriteEnabled = false;
        binary.HasInMemoryStore<MappedIndexerSeed, int>(row => row.Id, row => new(row.Id, row.Text, row.Data.ToArray()))
            .MapInMemoryEntity<MappedIndexerSeed>(row => new MappedBinaryIndexerRow { Id = row.Id })
            .MapInMemoryProperty<MappedIndexerSeed, byte[]>("Data", row => row.Data);
        binary.HasQuery("Read").From(sources => sources.Set<MappedBinaryIndexerRow>().Select(row => row["Data"])).List();
        var design = builder.Build();
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var fixture = Activator.CreateInstance(fakeAssembly.GetType(MappedReadTestHarness.Namespace + ".Testing.MappedIndexerContextRepositoryFixture")!)!;
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        foreach (var name in new[] { nameof(MappedTextIndexerRow), nameof(MappedBinaryIndexerRow) })
        {
            ModelFilterTestHarness.Store<MappedIndexerSeed>(data, name).Seed(new(1, "raw text", bytes));
        }
        var fakeText = Activator.CreateInstance(fakeAssembly.GetType(MappedReadTestHarness.Namespace + ".Testing.MappedTextIndexerRowRepositoryTestDouble")!, fixture)!;
        var fakeBinary = Activator.CreateInstance(fakeAssembly.GetType(MappedReadTestHarness.Namespace + ".Testing.MappedBinaryIndexerRowRepositoryTestDouble")!, fixture)!;
        var realAssembly = SavedResultTestHarness.CompileProduction(context, design);
        var realText = Activator.CreateInstance(realAssembly.GetType(MappedReadTestHarness.Namespace + ".Persistence.MappedTextIndexerRowRepositoryOperations")!, context)!;
        var realBinary = Activator.CreateInstance(realAssembly.GetType(MappedReadTestHarness.Namespace + ".Persistence.MappedBinaryIndexerRowRepositoryOperations")!, context)!;
        (await Invoke<Task<IReadOnlyList<string>>>(fakeText, "ReadAsync")).Should().Equal(await Invoke<Task<IReadOnlyList<string>>>(realText, "ReadAsync"));
        (await Invoke<Task<IReadOnlyList<byte[]>>>(fakeBinary, "ReadAsync")).Single().Should().Equal((await Invoke<Task<IReadOnlyList<byte[]>>>(realBinary, "ReadAsync")).Single());
        (await Invoke<Task<IReadOnlyList<byte[]>>>(fakeBinary, "ReadAsync")).Single()[0] = 99;
        (await Invoke<Task<IReadOnlyList<byte[]>>>(fakeBinary, "ReadAsync")).Single().Should().Equal(1, 2);
    }
}
