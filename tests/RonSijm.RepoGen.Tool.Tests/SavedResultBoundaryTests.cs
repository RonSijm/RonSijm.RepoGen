using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultBoundaryTests
{
    [Fact]
    public void SavedNavigationResultsCannotUseAnUnselectedRelatedFixtureStore()
    {
        using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder();
        builder.Entity<MemoryCollectionChild>().GenerateTestDouble(false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Navigation source*enabled, selected*");
    }

    [Fact]
    public void SavedReadsDoNotBroadenMutationPredicatesToNavigationDependentWrites()
    {
        using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder();
        builder.Entity<MemoryCollectionParent>().HasCommand("NavigationWrite").Where(row => row.Children.Any(child => !child.Hidden))
            .Patch(setters => setters.SetProperty(row => row.Name, "x")).Save().ReturnsSaved(row => row.Name);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Navigation-dependent write selection/mapping*");
    }

    [Fact]
    public void ExplicitOverrideRemainsScriptOnlyAndDoesNotClaimSavedQueryCoverage()
    {
        using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder();
        builder.Entity<MemoryCollectionParent>().HasCommand("ScriptOnly").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "x")).Save().ReturnsSaved(row => row.Children.Count)
            .RequireTestOverride("Application-controlled result read.");
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: builder.Build());
        var command = model.Entities.Single(entity => entity.EntityName == nameof(MemoryCollectionParent)).Commands.Single(command => command.MethodName == "ScriptOnlyAsync");
        command.InMemoryCommand!.Supported.Should().BeFalse(); command.InMemoryCommand.Reasons.Should().Contain("Designer requires a test override: Application-controlled result read.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
}
