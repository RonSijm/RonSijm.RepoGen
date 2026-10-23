using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using Writes = RonSijm.RepoGen.Tool.Tests.ModelWriteFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterGuardTests
{
    private static RepositoryModelBuilder GuardBuilder()
    {
        var builder = ModelFilterTestHarness.Base(strict: false, queries: false).Configure(options => options.UseInMemoryTestCommands());
        builder.Entity<ModelFilterRow>().WriteEnabled = true;
        builder.Entity<ModelFilterRow>().MapInMemoryWrites<ModelFilterRowSeed>((row, saved) => new(row.Id, row.Title, row.Deleted, row.Hidden, row.FolderId));
        return builder;
    }

    [Theory]
    [InlineData("context")]
    [InlineData("capture")]
    [InlineData("collection")]
    [InlineData("clock")]
    [InlineData("static")]
    [InlineData("helper")]
    [InlineData("identity")]
    [InlineData("navigation")]
    [InlineData("shadow")]
    [InlineData("unmapped")]
    [InlineData("autoinclude")]
    public void NonportableWriteFiltersRetainStrictCoverageAndExplicitScriptsWithoutInvokingState(string mode)
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, mode);
        var builder = GuardBuilder();
        if (mode == "shadow") builder.Entity<ModelFilterRow>().HasInMemoryStore(row => row.Id, row => new ModelFilterRow
            { Id = row.Id, Title = row.Title, Deleted = row.Deleted, Hidden = row.Hidden, FolderId = row.FolderId });
        var command = builder.Entity<ModelFilterRow>().HasCommand("Rename").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save().UseInMemoryModelQueryFilter();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        var coverage = model.Entities.Single(entity => entity.EntityName == "ModelFilterRow").Commands.Single().InMemoryCommand!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains(mode == "autoinclude" ? "AutoInclude" : "Model query filter", StringComparison.Ordinal));
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.RequireTestOverride("Context/provider filter behavior is explicitly scripted.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void FilterTranslationsRequireSeparatePerCommandOptInAndMappedMemberCoverage()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "helper");
        var builder = GuardBuilder().Configure(options => options.RequirePortableTestCommands());
        var command = builder.Entity<ModelFilterRow>().HasCommand("Rename").Where(row => row.Id == 1)
            .Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save().UseInMemoryModelQueryFilter();
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.UseInMemoryTranslations(); var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ModelFilterGuardCalls.Accept");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("AcceptedTitle").And.Contain("PerCommandUseInMemoryModelQueryFilter");
        builder.Entity<ModelFilterRow>().MapInMemoryEntity<ModelFilterRowSeed>(row => new ModelFilterRow { Id = row.Id, Deleted = row.Deleted, Hidden = row.Hidden, FolderId = row.FolderId });
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*Every EF scalar*");
    }

    [Fact]
    public void FilteredAtomicInsertRequiresItsOwnPermissionsWithoutFilteringTheInsert()
    {
        using var context = Writes.Context(); var builder = Writes.Builder(strict: false, queries: false).Configure(options => options.UseReturningWrites<SqliteReturningWriter>().UseAtomicWrites<SqliteAtomicWriter>());
        var entity = builder.Entity<ModelWriteFilterNote>(); var command = entity.HasCommand("Native").Create<ModelWriteFilterPatch>(request => new ModelWriteFilterNote
            { Id = request.Id, Title = request.Title, Version = 0, Deleted = request.Deleted, Hidden = request.Hidden })
            .AllowWrite(row => row.Id).AllowWrite(row => row.Version).InsertIfAbsent(row => row.Id).Returns(row => row.Title);
        command.SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        var coverage = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single(item => item.EntityName == "ModelWriteFilterNote")
            .Commands.Single(item => item.MethodName == "NativeAsync").InMemoryCommand!;
        coverage.Supported.Should().BeTrue(); coverage.ModelQueryFilter.Should().NotBeNull();
        builder.Configure(options => options.RequirePortableTestCommands());
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        command.UseInMemoryModelQueryFilter(false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void FilteredSavedNavigationGraphsDoNotBecomePortableFromTheCommandOptIn()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = GuardBuilder().Configure(options => options.UseInMemoryTestQueries().RequirePortableTestCommands());
        var entity = builder.Entity<ModelFilterRow>(); entity.HasInMemoryNavigation(row => row.Folder);
        entity.HasCommand("SavedGraph").Where(row => row.Id == 1).Patch(setters => setters.SetProperty(row => row.Title, "changed"))
            .Save().ReturnsSaved(row => row.Folder == null ? -1 : row.Folder.Id).UseInMemoryModelQueryFilter();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void FilteredGraphReconciliationRequiresAnOverrideEvenWithPortableScalarOwnerFilter()
    {
        using var context = new ModelWriteFilterGraphContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.FilteredGraphs.Contracts").UseScriptedTestDoubles("Generated.FilteredGraphs.Testing")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        parent.HasCommand("Reconcile").Where(row => row.Id == 1).Patch(setters => setters.SetProperty(row => row.Name, "changed"))
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children, request => request.Items!, row => row.Key, item => item.Key,
                children => children.MaximumItems(3).Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)))
            .SimulateCollectionWrites().UseInMemoryModelQueryFilter().Save();
        builder.Entity<MemoryCollectionChild>().HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*filtered graphs*");
    }
}
