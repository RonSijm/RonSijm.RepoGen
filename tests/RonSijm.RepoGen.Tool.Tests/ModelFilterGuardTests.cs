using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ModelFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterGuardTests
{
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
    public void NonportableModelFiltersFailCoverageWithoutInvokingContextGettersOrHelpers(string mode)
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, mode);
        var builder = Base(strict: false); var query = builder.Entity<ModelFilterRow>().HasQuery("Read").Select(row => row.Id).List();
        var entity = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single(item => item.EntityName == "ModelFilterRow");
        var coverage = entity.Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains(mode == "autoinclude" ? "AutoInclude" : "Model query filter", StringComparison.Ordinal));
        builder.Configure(options => options.RequirePortableTestQueries());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        query.RequireTestOverride("This filter requires deliberate application/provider test behavior.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ModelFilterHelperRequiresAnExactScalarTranslationAndRetainsMappedMemberValidation()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "helper");
        var builder = Base(); builder.Entity<ModelFilterRow>().HasQuery("Read").Select(row => row.Id).List();
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ModelFilterGuardCalls.Accept");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("AcceptedTitle").And.Contain("modelQueryFilters");
        builder.Entity<ModelFilterRow>().MapInMemoryEntity<ModelFilterRowSeed>(row => new ModelFilterRow { Id = row.Id, Deleted = row.Deleted, Hidden = row.Hidden, FolderId = row.FolderId });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*does not map scalar member 'Title'*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActiveFilteredNavigationGraphsStillRequireOverridesButGlobalIgnoreIsSupported(bool mapped)
    {
        using var context = Context(); var builder = Base(mapped, strict: false); var entity = builder.Entity<ModelFilterRow>();
        entity.HasInMemoryNavigation(row => row.Folder);
        var query = entity.HasQuery("Folder"); query.Select(row => row.Folder == null ? -1 : row.Folder.Id).List();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single(item => item.EntityName == "ModelFilterRow").Queries.Single().InMemoryQuery!.Supported.Should().BeFalse();
        query.IgnoreQueryFilters(); builder.Configure(options => options.RequirePortableTestQueries());
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ReadOptInDoesNotSilentlyEnableFilteredCommandSimulation()
    {
        using var context = Context(); var builder = Base(mapped: false, strict: false);
        builder.Configure(options => options.UseInMemoryTestCommands()); var entity = builder.Entity<ModelFilterRow>(); entity.WriteEnabled = true;
        entity.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single(item => item.EntityName == "ModelFilterRow").Commands.Single().InMemoryCommand!.Supported.Should().BeFalse();
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
