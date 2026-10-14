using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void FreshBatchProjectionsCompileInAllLayouts(RepositoryOutputLayout layout)
    {
        using var context = SavedBatchTestHarness.Context(); var design = SavedBatchTestHarness.Builder().Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        var testing = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        testing.Should().Contain("InMemorySavedBatchResults.Project").And.Contain("InMemoryRelationships.LoadCollection");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("OneResultPerInputInInputOrder").And.Contain("OneCoordinatedSnapshotForAllSavedInputKeys");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MissingNavigationCapabilitiesRequireAnOverride(bool navigation, bool queries)
    {
        using var context = SavedBatchTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, SavedBatchTestHarness.Builder(navigation, queries).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Navigation*");
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("delete")]
    [InlineData("reload")]
    public void InvalidSavedBatchLifecycleIsRejected(string mode)
    {
        using var context = SavedBatchTestHarness.Context(); var builder = SavedBatchTestHarness.Builder();
        var command = builder.Entity<SavedBatchParent>().HasCommand("Invalid")
            .PatchMany<SavedBatchItem, string, SavedBatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 3).Save().ReturnsSaved(row => row.Name);
        if (mode == "stage") command.Stage();
        if (mode == "delete") command.TrackedDelete();
        if (mode == "reload") command.ReloadAfterSave();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3701:*");
    }

    [Fact]
    public void OptingOutOfFixtureCommandsDoesNotChangeProductionOrContracts()
    {
        using var context = SavedBatchTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, SavedBatchTestHarness.Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, SavedBatchTestHarness.Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("InMemorySavedBatchResults.Project");
    }

    [Fact]
    public void OrdinaryBatchResultsStillRejectNavigationGraphs()
    {
        using var context = SavedBatchTestHarness.Context(); var builder = SavedBatchTestHarness.Builder();
        builder.Entity<SavedBatchParent>().HasCommand("Ordinary")
            .PatchMany<SavedBatchItem, string, SavedBatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 3).Save().Returns(row => row.Children.Count);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3002:*");
    }

    [Fact]
    public void ScalarSavedBatchResultsDoNotRequireTheReadBackend()
    {
        using var context = SavedBatchTestHarness.Context(); var builder = SavedBatchTestHarness.Builder(queries: false, strict: false);
        builder.Entity<SavedBatchParent>().HasCommand("Scalar")
            .PatchMany<SavedBatchItem, string, SavedBatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 3).Save().ReturnsSaved(row => row.Name);
        var design = builder.Build(); var model = RepositoryModelFactory.Create(context, typeof(SavedBatchContext).Assembly, design: design);
        model.Entities.Single(entity => entity.EntityName == nameof(SavedBatchParent)).Commands.Single(command => command.MethodName == "ScalarAsync")
            .InMemoryCommand!.Supported.Should().BeTrue();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design)); SavedResultTestHarness.CompileProduction(context, design);
    }
}
