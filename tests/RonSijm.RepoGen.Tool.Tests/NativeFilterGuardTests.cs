using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterGuardTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void BothImmediateSimulationAndCommandFilterPermissionsAreRequired(bool mapped, bool missingSimulation)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        var builder = Builder(mapped, simulate: !missingSimulation, filter: missingSimulation);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("capture")]
    [InlineData("clock")]
    [InlineData("helper")]
    [InlineData("shadow")]
    public void NonportableFiltersRemainUncoveredWithoutEvaluatingGettersOrHelpers(string mode)
    {
        using var context = new NativeFilterGuardContext(new DbContextOptionsBuilder<NativeFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, NativeFilterCacheKeyFactory>().Options, mode);
        var builder = Base(mapped: false, queries: false); var command = builder.Entity<ModelWriteFilterNote>().HasCommand("Native")
            .Where(row => row.Id > 0).Update(setters => setters.SetProperty(row => row.Title, "changed")).ReturnsMany(row => row.Title)
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        if (mode == "shadow")
        {
            // Production shadow capture needs its own opt-in; fixture simulation/filter permission
            // cannot turn an unapproved provider contract into a valid operation.
            generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*Shadow values require IncludeShadowProperties*");
            return;
        }
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.RequireTestOverride("Filter behavior is explicitly scripted for this provider/context.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ExactFixtureHelperTranslationStillNeedsCommandPermissionAndDoesNotAddSqlClientFallback()
    {
        using var context = new NativeFilterGuardContext(new DbContextOptionsBuilder<NativeFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, NativeFilterCacheKeyFactory>().Options, "helper");
        var builder = Base(queries: false); var command = builder.Entity<ModelWriteFilterNote>().HasCommand("Native")
            .Where(row => row.Id > 0).Update(setters => setters.SetProperty(row => row.Title, "changed")).ReturnsMany(row => row.Title)
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        builder.HasInMemoryTranslation<string, bool>("AcceptedTitle", title => ModelFilterGuardCalls.Accept(title), title => title != "deleted");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.UseInMemoryTranslations(); var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("AcceptedTitle");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ModelFilterGuardCalls.Accept");
        // Production still delegates the original EF model filter to SQL translation; no fixture recipe is injected.
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Repositories).Contents.Should().NotContain("AcceptedTitle");
    }

    [Fact]
    public void NativeGraphResultsRemainUnsupported()
    {
        using var context = ModelFilterTestHarness.Context(); var builder = SavedGraphFilterTestHarness.Base()
            .Configure(options => options.UseReturningWrites<SqliteReturningWriter>());
        builder.Entity<ModelFilterRow>().HasCommand("Graph").Where(row => row.Id == 1).Update(setters => setters.SetProperty(row => row.Title, "changed"))
            .ReturnsMany(row => row.Folder == null ? -1 : row.Folder.Id).SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*not a mapped scalar*Native returned rows do not load navigations*");
    }

    [Fact]
    public void AbsentModelFiltersAddNoSource()
    {
        using var plain = new NativeFilterGuardContext(new DbContextOptionsBuilder<NativeFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, NativeFilterCacheKeyFactory>().Options, "plain");
        RepositoryGenerator.Generate(plain, Builder().Build()).Files.Should().BeEquivalentTo(RepositoryGenerator.Generate(plain, Builder(filter: false).Build()).Files);
    }

    [Fact]
    public async Task AcknowledgedFilteredNativeWritesStillRequireScriptsOnEmptyStoresWhenNotOptedIn()
    {
        using var context = ModelWriteFilterTestHarness.Context(); var builder = Base();
        builder.Entity<ModelWriteFilterNote>().HasCommand("Native").Where(row => row.Id > 0).Delete().ReturnsMany(row => row.Title)
            .SimulateImmediateWrites().RequireTestOverride("Deliberately unconfigured model filter requires a script.");
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, data) = Fixture(assembly: assembly); data.Clear();
        Func<Task> execute = async () => await Call<MutationResult<string>>(repository, "Native");
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }
}
