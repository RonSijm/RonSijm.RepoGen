using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void OptionalBatchesKeepProductionContractsApiAndCacheShapesAndAllLayoutsCompile(RepositoryOutputLayout layout)
    {
        using var context = MemoryWriteTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, new MemoryBatchRepositoryDesigner(), layout);
        var disabled = RepositoryGenerator.Generate(context, new MemoryBatchRepositoryDesigner(false), layout);
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(enabled);
        var first = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryBatchRepositoryDesigner().CreateModel());
        var second = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryBatchRepositoryDesigner(false).CreateModel());
        System.Text.Json.JsonSerializer.Serialize(first.Entities.SelectMany(entity => entity.Commands)).Should()
            .Be(System.Text.Json.JsonSerializer.Serialize(second.Entities.SelectMany(entity => entity.Commands)));
    }

    [Fact]
    public void ExistingGeneratedOnInsertKeysDoNotRequireGenerationForBatchUpdatesOrDeletes()
    {
        using var context = new MemoryGeneratedWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite("Data Source=:memory:").Options);
        var bundle = RepositoryGenerator.Generate(context, new MemoryBatchRepositoryDesigner());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().Contain("ExecuteBatchAsync");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public async Task AlternateUniqueKeysCanMatchAStoreWithADifferentPrimaryKey()
    {
        using var context = new MemoryBatchAlternateContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.AlternateBatch.Contracts")
            .UseScriptedTestDoubles("Generated.AlternateBatch.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<MemoryWriteNote>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        entity.HasCommand("Hide").PatchMany<MemoryWriteRequest, string, IReadOnlyList<MemoryWriteRequest>>(items => items, row => row.Title, item => item.Title,
            setters => setters.SetProperty(row => row.Hidden, row => true), maximumItems: 3).Save().Returns(row => new MemoryWriteView(row.Id, row.Title, row.Version));
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.AlternateBatch.TestDoubles.MemoryBatchAlternateContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.AlternateBatch.TestDoubles.MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        MemoryWriteTestHarness.NoteStore(data).SeedRange([new() { Id = 1, Title = "one", Version = 1 }, new() { Id = 2, Title = "two", Version = 1 }]);
        var result = await MemoryWriteTestHarness.Invoke<CommandResult<IReadOnlyList<MemoryWriteView>>>(repository, "HideAsync",
            (object)new MemoryWriteRequest[] { new(0, "two"), new(0, "one") });
        result.Status.Should().Be(CommandStatus.Applied); result.AffectedRows.Should().Be(2);
        result.Value!.Select(row => row.Id).Should().Equal(2, 1);
        MemoryWriteTestHarness.NoteStore(data).Find(1)!.Hidden.Should().BeTrue();
        MemoryWriteTestHarness.NoteStore(data).Find(2)!.Hidden.Should().BeTrue();
    }
}
