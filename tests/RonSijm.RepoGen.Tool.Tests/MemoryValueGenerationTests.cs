using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryValueGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void OptionalAdaptersCompileWithoutChangingProductionApiOrCacheShapes(RepositoryOutputLayout layout)
    {
        using var context = MemoryValueTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, new MemoryValueRepositoryDesigner(), layout);
        var disabled = RepositoryGenerator.Generate(context, new MemoryValueRepositoryDesigner(false), layout);
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        var first = RepositoryModelFactory.Create(context, typeof(MemoryValueContext).Assembly, design: new MemoryValueRepositoryDesigner().CreateModel());
        var second = RepositoryModelFactory.Create(context, typeof(MemoryValueContext).Assembly, design: new MemoryValueRepositoryDesigner(false).CreateModel());
        System.Text.Json.JsonSerializer.Serialize(first.Entities.SelectMany(entity => entity.Commands)).Should()
            .Be(System.Text.Json.JsonSerializer.Serialize(second.Entities.SelectMany(entity => entity.Commands)));
        ScriptedTestDoubleTestHarness.Compile(enabled);
        enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("ExplicitDesignerFactories").And.Contain("AtomicSavePreparationOnly");
    }

    [Fact]
    public void MissingAdaptersRemainStrictlyUncoveredAndReplacingAStoreClearsItsFactories()
    {
        using var context = MemoryValueTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, new MemoryValueRepositoryDesigner(adapters: false));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*generated values*");
        var model = new RepositoryModelBuilder(); var entity = model.Entity<MemoryValueRow>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id });
        entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => 1);
        entity.WithoutInMemoryGeneratedValue(row => row.Id);
        model.Build().Entities.Single().InMemoryStore!.GeneratedValues.Should().BeEmpty();
        entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => 1);
        Action duplicate = () => entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => 2);
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD3501:*");
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id });
        model.Build().Entities.Single().InMemoryStore!.GeneratedValues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("clock")]
    [InlineData("capture")]
    public void NonGeneratedOrAmbientFactoriesFailWithStableDiagnostic(string kind)
    {
        using var context = MemoryValueTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.InvalidValues")
            .UseScriptedTestDoubles("Generated.InvalidValues.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<MemoryValueRow>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id, Title = row.Title, TitleLength = row.TitleLength, Label = row.Label });
        if (kind == "ordinary") entity.HasInMemoryGeneratedValue(row => row.Title, (row, existing) => "bad");
        else if (kind == "clock") entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => DateTime.Now.Year);
        else { var captured = string.Concat(kind, " value"); entity.HasInMemoryGeneratedValue(row => row.Label, (row, existing) => captured); }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3501:*");
    }

    [Fact]
    public async Task DeleteOfKnownGeneratedTokensDoesNotNeedAnyGenerationAdapter()
    {
        using var context = MemoryValueTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.DeleteValues")
            .UseScriptedTestDoubles("Generated.DeleteValues.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<MemoryValueRow>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryValueRow { Id = row.Id, Title = row.Title, TitleLength = row.TitleLength, Label = row.Label });
        entity.HasCommand("Delete").Where<MemoryValueRequest>((row, request) => row.Id == request.Id)
            .ExpectVersion<int, MemoryValueRequest>(row => row.TitleLength, (row, request) => request.ExpectedLength).TrackedDelete().Save();
        entity.ReadEnabled = false;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.DeleteValues.TestDoubles.MemoryValueContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.DeleteValues.TestDoubles.MemoryValueRowRepositoryTestDouble")!, fixture)!;
        var data = (RonSijm.RepoGen.Testing.InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (RonSijm.RepoGen.Testing.InMemoryStore<MemoryValueRow, int>)data.GetType().GetProperty("MemoryValueRowStore")!.GetValue(data)!;
        store.Seed(new() { Id = 7, Title = "old", TitleLength = 3 });
        (await MemoryWriteTestHarness.Invoke<CommandResult>(repository, "DeleteAsync", new MemoryValueRequest(7, ""))).Status.Should().Be(CommandStatus.Applied);
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task CheckedSeedAwareAllocationFailsWithoutWrappingOrStaging()
    {
        using var context = MemoryValueTestHarness.Context();
        var (repository, data, store) = MemoryValueTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryValueRepositoryDesigner())));
        store.Seed(new() { Id = int.MaxValue, Title = "max", TitleLength = 3 });
        Func<Task> create = () => MemoryWriteTestHarness.Invoke<CommandResult<MemoryValueView>>(repository, "CreateAsync", new MemoryValueRequest(0, "new"));
        await create.Should().ThrowAsync<OverflowException>();
        data.PendingCount.Should().Be(0); store.Count.Should().Be(2);
    }
}
