using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExplicitSimulationLeavesProductionContractsAndApiUnchangedAndCompilesAllLayouts(RepositoryOutputLayout layout)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner(), layout);
        var disabled = RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner(simulate: false, strict: false), layout);
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(enabled);
        enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("SimulateCollectionWrites").And.Contain("OwnerScopedVisibleSavedChildren");
    }

    [Fact]
    public void SimulationIsOffByDefaultAndStrictModeReportsWhyAnOverrideIsRequired()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner(simulate: false, strict: false));
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ReconcileAsync");
        Action strict = () => RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner(simulate: false));
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Graph reconciliation*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingChildStoreOrGeneratedValueFactoryIsAnExplicitCoverageGap(bool childStore)
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action action = () => RepositoryGenerator.Generate(context, new MemoryCollectionRepositoryDesigner(childStore: childStore, generatedValues: false));
        action.Should().Throw<InvalidOperationException>().WithMessage(childStore ? "RGD3402:*Generated child values*" : "RGD3402:*writable child HasInMemoryStore*");
    }

    [Fact]
    public void ActiveEfChildFiltersAreNotSilentlyReplacedByDesignerVisibility()
    {
        using var context = new MemoryCollectionFilteredContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: new MemoryCollectionRepositoryDesigner(strict: false).CreateModel());
        var coverage = model.Entities.Single(entity => entity.EntityName == nameof(MemoryCollectionParent)).Commands.Single(command => command.MethodName == "SaveItemsAsync").InMemoryCommand!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains("EF filters/AutoInclude child", StringComparison.Ordinal));
    }
}
