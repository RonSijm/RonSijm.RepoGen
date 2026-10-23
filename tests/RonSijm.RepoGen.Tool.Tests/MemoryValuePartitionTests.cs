using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryValuePartitionTests
{
    [Fact]
    public async Task GeneratedReservationsDefaultsAndSavesAreLocalToTheSelectedPartition()
    {
        using var context = MemoryValueTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.PartitionValues.Contracts")
            .UseScriptedTestDoubles("Generated.PartitionValues.TestDoubles").UseKeyedContextLeases<string>()
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<MemoryValueRow>(); entity.ReadEnabled = false;
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id, Title = row.Title, TitleLength = row.TitleLength, Label = row.Label });
        entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
        entity.HasInMemoryGeneratedValue(row => row.Label, (row, existing) => "fixture");
        entity.HasInMemoryGeneratedValue(row => row.TitleLength, (row, existing) => row.Title.Length);
        entity.HasCommand("Stage").Create<MemoryValuePartitionRequest>(request => new MemoryValueRow { Title = request.Title })
            .UseContextKey<MemoryValuePartitionRequest, string>(request => request.Key).Stage();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        const string ns = "Generated.PartitionValues.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryValueContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "MemoryValueRowRepositoryTestDouble")!, fixture)!;
        var first = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["B"])!;
        static InMemoryStore<MemoryValueRow, int> Store(InMemoryWriteDataContext data)
            => (InMemoryStore<MemoryValueRow, int>)data.GetType().GetProperty("MemoryValueRowStore")!.GetValue(data)!;
        Store(first).Seed(new() { Id = 7, Title = "A", TitleLength = 1 });
        Store(second).Seed(new() { Id = 100, Title = "B", TitleLength = 1 });
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryValuePartitionRequest("A", "eight"));
        await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "StageAsync", new MemoryValuePartitionRequest("B", "hundred one"));
        await first.SaveChangesAsync();
        Store(first).Find(8)!.Label.Should().Be("fixture"); Store(second).Find(101).Should().BeNull(); second.PendingCount.Should().Be(1);
        await second.SaveChangesAsync(); Store(second).Find(101)!.TitleLength.Should().Be(11);
    }
}
