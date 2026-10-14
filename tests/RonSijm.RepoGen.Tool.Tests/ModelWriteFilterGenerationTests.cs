using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using Writes = RonSijm.RepoGen.Tool.Tests.ModelWriteFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void PerCommandFiltersCompileInAllLayoutsAndBothStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Writes.Context(); var design = Writes.Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("PerCommandUseInMemoryModelQueryFilter")
            .And.Contain("SharedSaveDoesNotRecheckQueryFilter").And.Contain("NoInsertFilter").And.Contain("ReappliedToFreshSavedRows");
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("EntityFrameworkCore");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteFilterOptInCannotChangeProductionContractsOrApi(bool mapped)
    {
        using var context = Writes.Context();
        var on = RepositoryGenerator.Generate(context, Writes.Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Writes.Builder(mapped, enabled: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(on.CreateApiSnapshot().ToJson());
        off.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("PerCommandUseInMemoryModelQueryFilter");
    }

    [Fact]
    public void ReadAndWriteOptInsAndTheirBackendsRemainIndependent()
    {
        using var context = Writes.Context();
        var readOnly = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: Writes.Builder(enabled: false, strict: false).Build());
        var entity = readOnly.Entities.Single(item => item.EntityName == "ModelWriteFilterNote");
        entity.Queries.Should().OnlyContain(query => query.InMemoryQuery!.Supported); entity.Commands.Should().OnlyContain(command => !command.InMemoryCommand!.Supported);
        var writes = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: Writes.Builder(readFilters: false).Build()).Entities.Single(item => item.EntityName == "ModelWriteFilterNote");
        writes.Commands.Should().OnlyContain(command => command.InMemoryCommand!.Supported);
        writes.Queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!.Supported.Should().BeFalse();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Writes.Builder(queries: false).Build()));
    }

    [Fact]
    public void DisabledBackendAndAbsentFilterAddNoOutputAndPerCommandSettingCanBeRemoved()
    {
        using var context = Writes.Context(); var builder = Writes.Builder(backend: false);
        var toggle = builder.Entity<ModelWriteFilterNote>().HasCommand("Toggle").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty(row => row.Title, "toggle")).Save().UseInMemoryModelQueryFilter();
        var baseline = RepositoryGenerator.Generate(context, builder.Build());
        toggle.UseInMemoryModelQueryFilter(false);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(baseline.Files);
        using var plain = MemoryWriteTestHarness.Context(); var simple = InMemoryCommandTranslationTestHarness.Builder();
        var noFilter = simple.Entity<MemoryWriteNote>().HasCommand("Toggle").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty(row => row.Title, "toggle")).Save().UseInMemoryTranslations();
        var before = RepositoryGenerator.Generate(plain, simple.Build());
        noFilter.UseInMemoryModelQueryFilter();
        RepositoryGenerator.Generate(plain, simple.Build()).Files.Should().BeEquivalentTo(before.Files);
        builder = Writes.Builder(); builder.Entity<ModelWriteFilterNote>().HasCommand("Toggle").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty(row => row.Title, "toggle")).Save().UseInMemoryModelQueryFilter().UseInMemoryModelQueryFilter(false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
