using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeSavedGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void BoundedFreshUpdateResultsCompileAcrossLayouts(RepositoryOutputLayout layout)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        source.Should().Contain("ExecuteUpdateWithResultQueryAsync").And.Contain("SavedBatchQuery.Restrict").And.Contain("SavedBatchQuery.InInputOrder");
        var testing = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        testing.Should().Contain("ExecuteUpdateWithSavedResultsAsync").And.Contain("var __repoGenSnapshot = __repoGenSavedSnapshot;");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("nativeSavedResultQuery").And.Contain("AbortAndRollbackNotTakeOrPreselection");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        CompileProduction(context, builder, layout);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GraphsRetainSeparateNavigationAndReadPermission(bool navigations, bool queries)
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(navigations, queries).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void ScalarFreshResultsNeedNoAutomaticReadBackend()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(navigations: false, queries: false, scalarOnly: true).Build());
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void TestBackendOptOutDoesNotChangeProductionContractsOrApi()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
        {
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        }
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveBoundsAreRejectedImmediately(int bound)
    {
        var entity = new RepositoryModelBuilder().Entity<MemoryCollectionChild>();
        Action configure = () => entity.HasCommand("Invalid").ReturnsSavedMany(row => row.Text, bound);
        configure.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("Stage")]
    [InlineData("Save")]
    [InlineData("Reload")]
    [InlineData("Atomic")]
    public void FreshManyResultsRetainExplicitLifecycleGuards(string mode)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        var command = builder.Entity<MemoryCollectionChild>().HasCommand("Invalid")
            .Update(setters => setters.SetProperty(row => row.Text, "changed")).ReturnsSavedMany(row => row.Text, maximumRows: 3);
        switch (mode)
        {
            case "Delete": command.Delete(); break;
            case "Stage": command.Stage(); break;
            case "Save": command.Save(); break;
            case "Reload": command.ReloadAfterSave(); break;
            case "Atomic": command.InsertIfAbsent(row => row.Id); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3701:*");
    }

    [Fact]
    public async Task MissingImmediateConsentRetainsTypedOverridesEvenForEmptyStores()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder(strict: false);
        builder.Entity<MemoryCollectionChild>().HasCommand("NoSimulation")
            .Update(setters => setters.SetProperty(row => row.Text, "changed")).ReturnsSavedMany(row => row.Text, maximumRows: 3);
        var (repository, _, data) = Fixture(Compile(builder));
        MemoryCollectionTestHarness.ChildStore(data).Clear();
        Func<Task> unsupported = () => MappedStateTestHarness.Call<MutationResult<string>>(repository, "NoSimulationAsync");
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnNoSimulationAsync", new MutationResult<string>(["scripted"]));
        (await MappedStateTestHarness.Call<MutationResult<string>>(repository, "NoSimulationAsync")).Items.Should().Equal("scripted");
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void FreshNativeResultsRequireTheExplicitReturningProvider()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder().Configure(options => options.UseReturningWrites<SqliteReturningWriter>(false));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1305:*");
    }

    [Fact]
    public void ChoosingReturnsManyAgainClearsFreshModeAndBound()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        builder.Entity<MemoryCollectionChild>().HasCommand("Latest")
            .Update(setters => setters.SetProperty(row => row.Text, "changed"))
            .ReturnsSavedMany(row => row.Parent!.Name, maximumRows: 3).ReturnsMany(row => row.Text).SimulateImmediateWrites();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: builder.Build());
        var command = model.Entities.Single(entity => entity.EntityName == nameof(MemoryCollectionChild)).Commands.Single(command => command.MethodName == "LatestAsync");
        command.SavedResultKeys.Should().BeNull();
        command.SavedResultMaximumRows.Should().BeNull();
        command.InMemoryCommand!.Supported.Should().BeTrue();
    }
}
