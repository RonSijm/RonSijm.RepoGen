using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SavedResultsGenerateClosedContractsAndExplicitFreshReadsInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = SavedResultTestHarness.Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        var testing = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        testing.Should().Contain("__repoGenSavedEntity =>").And.Contain("InMemoryRelationships.LoadCollection").And.Contain("InMemoryRelationships.LoadReference");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("savedResultQuery").And.Contain("WriteAlreadySaved");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, builder.Build());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NavigationResultsRequireExplicitDeclarationsAndReadBackend(bool navigations, bool queries)
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action action = () => RepositoryGenerator.Generate(context, SavedResultTestHarness.Builder(navigations, queries).Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Navigation*");
    }

    [Fact]
    public void MissingDeclarationsHaveHonestScriptCoverageWhenStrictCheckingIsDisabled()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, SavedResultTestHarness.Builder(navigations: false, strict: false).Build());
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("RequiresOverride").And.Contain("savedResultQuery"); // Only scalar result queries are covered.
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void TestBackendOptOutDoesNotChangeProductionOrContracts()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, SavedResultTestHarness.Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, SavedResultTestHarness.Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("__repoGenSavedEntity =>");
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("delete")]
    [InlineData("reload")]
    [InlineData("immediate")]
    public void SavedReadsRejectInvalidWriteLifecycleBeforeRendering(string mode)
    {
        using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder();
        var command = builder.Entity<MemoryCollectionParent>().HasCommand("Invalid").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "x")).Save().ReturnsSaved(row => row.Name);
        if (mode == "stage") command.Stage();
        if (mode == "delete") command.TrackedDelete();
        if (mode == "reload") command.ReloadAfterSave();
        if (mode == "immediate") command.Update(setters => setters.SetProperty(row => row.Name, "x"));
        Action action = () => RepositoryGenerator.Generate(context, builder.Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3701:*");
    }

    [Fact]
    public void ChoosingReturnsAgainResetsSavedQueryMode()
    {
        using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder();
        builder.Entity<MemoryCollectionParent>().HasCommand("LatestChoice").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "x")).Save().ReturnsSaved(row => row.Children.Count).Returns(row => row.Name);
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: builder.Build());
        var command = model.Entities.Single(entity => entity.EntityName == nameof(MemoryCollectionParent)).Commands.Single(command => command.MethodName == "LatestChoiceAsync");
        command.SavedResultKeys.Should().BeNull(); command.InMemoryCommand!.Supported.Should().BeTrue();
    }
}
