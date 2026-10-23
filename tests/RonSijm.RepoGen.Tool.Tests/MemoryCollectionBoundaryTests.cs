using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionBoundaryTests
{
    private static RepositoryModelBuilder Builder(bool writable = true, bool selected = true, bool complete = true, bool additions = true,
        bool factory = true, bool navigationResult = false, string? overrideReason = null)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.CollectionBoundary.Contracts")
            .UseScriptedTestDoubles("Generated.CollectionBoundary.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        var child = builder.Entity<MemoryCollectionChild>(); child.WriteEnabled = writable; child.GenerateTestDouble(selected);
        child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        if (factory) child.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        var command = parent.HasCommand("Replace").Where<MemoryCollectionRequest>((row, request) => row.Id == request.Id)
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, request => request.Items!, row => row.Key, item => item.Key,
                children =>
                {
                    children.MaximumItems(3).AllowWrite(row => row.Version).Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text));
                    if (additions)
                    {
                        if (complete) children.Add(item => new MemoryCollectionChild { Key = item.Key, Text = item.Text, Hidden = false, Version = 1 });
                        else children.Add(item => new MemoryCollectionChild { Key = item.Key, Text = item.Text });
                    }
                }).Save().SimulateCollectionWrites();
        if (navigationResult) command.Returns(row => row.Children.Count);
        if (overrideReason is not null) command.RequireTestOverride(overrideReason);
        return builder;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ChildStoresMustBeWritableAndSelectedEvenIfNoChildPortIsInvoked(bool writable, bool selected)
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action action = () => RepositoryGenerator.Generate(context, Builder(writable: writable, selected: selected).Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*writable child HasInMemoryStore*");
    }

    [Fact]
    public void PersistenceConstructorDefaultsAreNeverInferredForChildInsertion()
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action action = () => RepositoryGenerator.Generate(context, Builder(complete: false).Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*explicitly assign scalar*constructor/database defaults*");
    }

    [Fact]
    public void ExistingGeneratedIdsNeedNoFactoryWhenChildAdditionIsDisabled()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(additions: false, factory: false).Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().Contain("ReconcileAsync");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void NavigationDependentCommandResultsRemainExplicitOverrides()
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action action = () => RepositoryGenerator.Generate(context, Builder(navigationResult: true).Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Navigation-dependent write selection/mapping*");
    }

    [Fact]
    public void DesignerOverrideAcknowledgesUnsupportedGraphAndRetainsScriptOnlyExecution()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(navigationResult: true, overrideReason: "This result needs a domain graph.").Build());
        var testing = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents;
        testing.Should().NotContain("ReconcileAsync").And.Contain("This result needs a domain graph.");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride").And.NotContain("collectionSimulation");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void ACollectionOptInOnAScalarCommandIsNotTreatedAsSuccessfulSimulation()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        builder.Entity<MemoryCollectionParent>().HasCommand("Scalar").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "scalar")).Save().SimulateCollectionWrites();
        Action action = () => RepositoryGenerator.Generate(context, builder.Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*requires a designed tracked Patch*ReconcileCollection*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildModelsExcludedFromRepositoryGenerationCannotPretendToHaveFixtureStores(bool nested)
    {
        using var context = new MemoryExcludedCollectionContext(new DbContextOptionsBuilder<MemoryExcludedCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.ExcludedCollections.Contracts")
            .UseScriptedTestDoubles("Generated.ExcludedCollections.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<MemoryExcludedCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryExcludedCollectionParent { Id = row.Id });
        var command = parent.HasCommand("Replace").Where<MemoryCollectionRequest>((row, request) => row.Id == request.Id);
        if (nested)
        {
            builder.Entity<MemoryExcludedCollectionContext.NestedChild>().HasInMemoryStore(row => row.Id,
                row => new MemoryExcludedCollectionContext.NestedChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key });
            command.ReconcileCollection<MemoryExcludedCollectionContext.NestedChild, MemoryCollectionItem, string, MemoryCollectionRequest>(
                row => row.NestedChildren, request => request.Items!, row => row.Key, item => item.Key, children => children.MissingItems(RepositoryCollectionRemovalBehavior.Delete));
        }
        else
        {
            builder.Entity<MemoryGenericCollectionChild<int>>().HasInMemoryStore(row => row.Id,
                row => new MemoryGenericCollectionChild<int> { Id = row.Id, ParentId = row.ParentId, Key = row.Key });
            command.ReconcileCollection<MemoryGenericCollectionChild<int>, MemoryCollectionItem, string, MemoryCollectionRequest>(
                row => row.GenericChildren, request => request.Items!, row => row.Key, item => item.Key, children => children.MissingItems(RepositoryCollectionRemovalBehavior.Delete));
        }
        command.Save().SimulateCollectionWrites();
        Action action = () => RepositoryGenerator.Generate(context, builder.Build());
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*nested/generic child CLR type excluded from repository generation*");
    }
}
