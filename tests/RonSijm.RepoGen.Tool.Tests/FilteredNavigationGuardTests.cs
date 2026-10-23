using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredNavigationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationGuardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredPrincipalToDependentReferencesAreAlsoConservative(bool mapped)
    {
        using var context = new FilteredNavigationPrincipalContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = NavigationReadTestHarness.Builder(strict: true);
        if (mapped) ProxyNavigationRepositoryDesigner.Stores(builder); else NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadProfile>().UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Entity<NavigationReadLibrary>().HasQuery("Read").Select(row => row.Profile == null ? -1 : row.Profile.Id).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*required reference to a filtered target*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FilteredSkipNavigationsStillRequireGraphPermission(bool mapped)
    {
        using var context = new FilteredNavigationManyContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = ManyReadTestHarness.Builder(mapped);
        builder.Entity<ManyReadTag>().UseInMemoryModelQueryFilter();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Active EF model query filters*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EachFilteredParticipantNeedsTheNavigationOptIn(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(mapped);
        var query = builder.Entity<ModelFilterFolder>().HasQuery("Read"); query.Select(folder => folder.Rows.Count).List();
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(); // Row-only permission is not graph permission.
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Active EF model query filters*");
        query.RequireTestOverride("Application deliberately supplies filtered relationship behavior.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Entity<ModelFilterFolder>().ExcludeQuery("Read");
        builder.Entity<ModelFilterFolder>().HasQuery("Read").Select(folder => folder.Rows.Count).List();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredFilteredTargetsRetainExplicitRootPruningBoundaryAndIgnoreRemovesIt(bool mapped)
    {
        using var context = new FilteredNavigationRequiredContext(new DbContextOptionsBuilder<FilteredNavigationRequiredContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Base(mapped); var query = builder.Entity<ModelFilterRow>().HasQuery("Read"); query.Select(row => row.Folder == null ? -1 : row.Folder.Id).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*required reference to a filtered target*inner join*");
        query.IgnoreQueryFilters(); ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void RelatedFilterMembersMustBeMappedEvenWhenTheProjectionDoesNotUseThem()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base();
        builder.Entity<ModelFilterFolder>().HasQuery("Read").Select(folder => folder.Rows.Count).List();
        builder.Entity<ModelFilterRow>().MapInMemoryEntity<ModelFilterRowSeed>(row => new ModelFilterRow { Id = row.Id, Title = row.Title, Hidden = row.Hidden, FolderId = row.FolderId });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*does not map scalar member 'Deleted'*");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("navigation")]
    [InlineData("helper")]
    [InlineData("autoinclude")]
    public void NonportableRelatedFiltersAndImplicitLoadingDoNotBecomeSupported(string mode)
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, mode);
        var builder = Base(); builder.Entity<ModelFilterFolder>().HasQuery("Read").Select(folder => folder.Rows.Count).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
    }

    [Fact]
    public void ExplicitRelatedScalarTranslationIsValidatedWithoutChangingProduction()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "helper");
        var builder = Base(); builder.Entity<ModelFilterFolder>().HasQuery("Read").Select(folder => folder.Rows.Count).List();
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ModelFilterGuardCalls.Accept");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Repositories).Contents.Should().NotContain("AcceptedTitle");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("AcceptedTitle");
    }

    [Fact]
    public void NavigationReadOptInDoesNotEnableFilteredSavedGraphWrites()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(mapped: false, strict: false);
        builder.Configure(options => options.UseInMemoryTestCommands().RequirePortableTestCommands());
        var row = builder.Entity<ModelFilterRow>(); row.WriteEnabled = true;
        row.HasCommand("Rename").Where<int>((item, id) => item.Id == id).Patch(setters => setters.SetProperty(item => item.Title, "changed"))
            .UseInMemoryModelQueryFilter().ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF model query filters*");
    }
}
