using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SavedGraphFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedGraphFilterGuardTests
{
    [Fact]
    public void ScalarResultsNeedNoReadBackendAndUnusedGraphPermissionAddsNoOutput()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(queries: false);
        var command = builder.Entity<ModelFilterRow>().HasCommand("Scalar").Where(item => item.Id == 1)
            .Patch(setters => setters.SetProperty(item => item.Title, "changed")).UseInMemoryModelQueryFilter().Save().ReturnsSaved(item => item.Title);
        var baseline = RepositoryGenerator.Generate(context, builder.Build());
        command.UseInMemoryModelQueryFilter(includeNavigations: true);
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); bundle.Files.Should().BeEquivalentTo(baseline.Files);
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void SavedGraphPermissionDoesNotAuthorizeFilteredReconciliationWrites()
    {
        using var context = new ModelWriteFilterGraphContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.SavedGraphMutation.Contracts").UseScriptedTestDoubles("Generated.SavedGraphMutation.Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(item => item.Id, item => new MemoryCollectionParent { Id = item.Id, OwnerId = item.OwnerId, Name = item.Name, Version = item.Version });
        parent.HasCommand("Reconcile").Where(item => item.Id == 1).Patch(setters => setters.SetProperty(item => item.Name, "changed"))
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(item => item.Children, request => request.Items!, item => item.Key, input => input.Key,
                children => children.MaximumItems(3).Update(setters => setters.SetProperty(item => item.Text, (item, input) => input.Text)))
            .SimulateCollectionWrites().UseInMemoryModelQueryFilter(includeNavigations: true).Save();
        builder.Entity<MemoryCollectionChild>().HasInMemoryStore(item => item.Id, item => new MemoryCollectionChild { Id = item.Id, ParentId = item.ParentId, Key = item.Key, Text = item.Text, Version = item.Version, Hidden = item.Hidden });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*filtered graphs*");
    }

    private static RepositoryCommandBuilder<ModelFilterRow> Rename(RepositoryModelBuilder builder, bool graphFilters = true)
        => builder.Entity<ModelFilterRow>().HasCommand("Rename").Where(item => item.Id == 1)
            .Patch(setters => setters.SetProperty(item => item.Title, "changed")).UseInMemoryModelQueryFilter(includeNavigations: graphFilters).Save()
            .ReturnsSaved(item => new FilteredNavigationCard(item.Id, item.Title, item.Folder == null ? null : item.Folder.Id));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CommandOwnsItsRootPermissionEvenWhenRootReadFiltersAreDisabled(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(mapped);
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(false); Rename(builder);
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        var coverage = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities
            .Single(entity => entity.EntityName == "ModelFilterRow").Commands.Single().InMemoryCommand!;
        coverage.Supported.Should().BeTrue(); coverage.SavedResult!.ModelQueryFilters.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EntityGraphPermissionDoesNotEnableCommandGraphPermissionAndRelatedPermissionIsStillRequired(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(mapped); var command = Rename(builder, graphFilters: false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF model query filters*");
        command.UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF model query filters*");
        builder.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter(includeNavigations: true);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiredFilteredReferencesAndUnsafeOptionalDereferencesRetainOverrides(bool mapped)
    {
        using var required = new FilteredNavigationRequiredContext(new DbContextOptionsBuilder<FilteredNavigationRequiredContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Base(mapped); Rename(builder);
        Action generate = () => RepositoryGenerator.Generate(required, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*required reference to a filtered target*");
        using var optional = ModelFilterTestHarness.Context(); builder.Entity<ModelFilterRow>().ExcludeCommand("Rename");
        builder.Entity<ModelFilterRow>().HasCommand("Unsafe").Where(item => item.Id == 1).Patch(setters => setters.SetProperty(item => item.Title, "changed"))
            .UseInMemoryModelQueryFilter(includeNavigations: true).Save().ReturnsSaved(item => item.Folder!.Id);
        generate = () => RepositoryGenerator.Generate(optional, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*filtered optional target*local explicit null guards*");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("navigation")]
    [InlineData("autoinclude")]
    public void RelatedNonportablePoliciesAreNotEvaluatedOrAutomaticallyEnabled(string mode)
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, mode);
        var builder = Base(); builder.Entity<ModelFilterFolder>().HasCommand("Create")
            .Create<SavedGraphFilterFolderInput>(input => new ModelFilterFolder { Id = input.Id, Archived = input.Archived }).AllowWrite(item => item.Id)
            .UseInMemoryModelQueryFilter(includeNavigations: true).Save().ReturnsSaved(item => item.Rows.Count);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void RelatedFilterTranslationsNeedTheCommandsOwnTranslationPermission()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "helper");
        var builder = Base(); var command = builder.Entity<ModelFilterFolder>().HasCommand("Create")
            .Create<SavedGraphFilterFolderInput>(input => new ModelFilterFolder { Id = input.Id, Archived = input.Archived }).AllowWrite(item => item.Id)
            .UseInMemoryModelQueryFilter(includeNavigations: true).Save().ReturnsSaved(item => item.Rows.Count);
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.UseInMemoryTranslations(); var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("AcceptedTitle").And.Contain("savedModelQueryFilters");
    }

    [Fact]
    public void RelatedFilterMembersNeedACompletePublicReadMap()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(); Rename(builder);
        var folder = builder.Entity<ModelFilterFolder>(); folder.WriteEnabled = false; folder.WithoutInMemoryWrites();
        folder.MapInMemoryEntity<ModelFilterFolderSeed>(input => new ModelFilterFolder { Id = input.Id });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*does not map scalar member 'Archived'*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FilteredManyToManySavedGraphsRemainScripted(bool mapped)
    {
        using var context = new FilteredNavigationManyContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = ManyReadTestHarness.Builder(mapped, strict: false); var owner = builder.Entity<ManyReadOwner>();
        owner.ExcludeCommand("Rename"); owner.ExcludeCommand("RenameMany");
        owner.HasCommand("Rename").Where(item => item.Id == 1).Patch(setters => setters.SetProperty(item => item.Name, "changed"))
            .UseInMemoryModelQueryFilter(includeNavigations: true).Save().ReturnsSaved(item => item.Tags.Count);
        builder.Entity<ManyReadTag>().UseInMemoryModelQueryFilter(includeNavigations: true);
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Filtered many-to-many navigation graphs*");
    }

    [Fact]
    public void AbsentModelFiltersAreByteStableAndReadBackendIsStillRequiredForGraphs()
    {
        using var plain = new SavedGraphFilterPlainContext(new DbContextOptionsBuilder<SavedGraphFilterPlainContext>().UseSqlite("Data Source=:memory:").Options);
        RepositoryGenerator.Generate(plain, Builder().Build()).Files.Should().BeEquivalentTo(
            RepositoryGenerator.Generate(plain, Builder(graphFilters: false).Build()).Files);
        using var context = ModelFilterTestHarness.Context(); var builder = Base(queries: false); Rename(builder);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*ReturnsSaved navigation projections require UseInMemoryTestQueries*");
    }

    [Fact]
    public async Task AnAcknowledgedUncoveredSavedGraphNeverReturnsSuccessOnAnEmptyStore()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = Base(); Rename(builder, graphFilters: false).RequireTestOverride("Provider graph policy is deliberately scripted.");
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (rows, _, data) = Fixture(assembly: assembly); data.Clear();
        Func<Task> execute = async () => await Call<RonSijm.RepoGen.Abstractions.CommandResult<FilteredNavigationCard>>(rows, "Rename");
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }
}
