using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void CompleteOptInProducesGetterFreeResultsAndExplicitNonProviderReceipts(RepositoryOutputLayout layout)
    {
        using var context = MappedStateTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, MappedReturnTestHarness.Builder().Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.IdentifierToken)
            .Select(token => token.ValueText).Should().NotContain("EntityFrameworkCore").And.NotContain("EF")
            .And.NotContain("ReturningWriteRow").And.NotContain("__repoGenReturnedRow");
        source.Should().Contain("ExecuteUpdateReturningAsync<").And.Contain("ExecuteDeleteReturningAsync<")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.MappedStateNote");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("returningPropertyState").And.Contain("ExplicitScalarFixtureStateNotNativeReturnedBuffer")
            .And.Contain("MatchedPostUpdateScalars").And.Contain("MatchedPreDeleteScalars").And.Contain("\"providerEquivalence\": false");
    }

    [Fact]
    public void FixtureBackendChangesNeitherProductionSourcesNorContractsOrApi()
    {
        using var context = MappedStateTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, MappedReturnTestHarness.Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, MappedReturnTestHarness.Builder(commands: false).Build());
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("returningPropertyState");
    }

    [Fact]
    public async Task ReturnedValuesDoNotDependOnEnablingAutomaticQueries()
    {
        var (repository, _, store) = MappedStateTestHarness.Fixture(MappedReturnTestHarness.Compile(MappedReturnTestHarness.Builder(queries: false)));
        var result = await MappedStateTestHarness.Call<MutationResult<MappedStateCard>>(repository, "RenameReturnedAsync", new MappedStateRequest(1, "unused"));
        result.Items.Single().Revision.Should().Be(4);
        store.Find(1)!.Revision.Should().Be(4);
    }

    [Fact]
    public async Task StateConsentDoesNotImplyImmediateExecutionAndOverridesStillWinOnEmptyStores()
    {
        using var context = MappedStateTestHarness.Context();
        var builder = MappedReturnTestHarness.Builder(simulate: false, strict: false);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var (repository, _, store) = MappedStateTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        store.Clear();
        Func<Task> call = () => MappedStateTestHarness.Call<MutationResult<MappedStateCard>>(repository, "RenameReturnedAsync", new MappedStateRequest(1, "unused"));
        await call.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRenameReturnedAsync", new MutationResult<MappedStateCard>([]));
        (await MappedStateTestHarness.Call<MutationResult<MappedStateCard>>(repository, "RenameReturnedAsync", new MappedStateRequest(1, "unused"))).Items.Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryMappedStoresStillNeedSeparateWriteStateConsentEvenForClrOnlyResults()
    {
        using var context = ImmediateTestHarness.Context();
        var builder = ImmediateTestHarness.Builder(mapped: true);
        builder.Entity<ImmediateRow>().HasCommand("ReturnedState").Delete().ReturnsMany(row => row.Amount)
            .IncludeMappedProperties().SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*IncludeMappedProperties*");
        builder.Entity<ImmediateRow>().MapInMemoryWrites<ImmediateData>((row, saved) => new(row.Id, row.OwnerId, row.Code,
            row.Amount, row.Previous, row.Hidden, row.Version, row.Stamp.ToArray(), saved == null ? "created" : saved.Tag), useMappedPropertyState: true);
        // A capture permission also works on an ordinary complete map with no named slots.
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var model = RepositoryModelFactory.Create(context, typeof(ImmediateContext).Assembly, design: builder.Build());
        model.Entities.Single().Commands.Single(command => command.MethodName == "ReturnedStateAsync").InMemoryCommand!.ReturningState.Should().NotBeNull();
        assembly.Should().NotBeNull();
    }

    [Fact]
    public void WriteStateDoesNotRelaxProductionCapturePermissionOrWriterRequirements()
    {
        using var context = MappedStateTestHarness.Context();
        var builder = MappedStateTestHarness.Builder();
        builder.Entity<MappedStateNote>().HasCommand("ReturnedState").Delete().ReturnsMany(row => EF.Property<int>(row, "Revision"))
            .IncludeMappedProperties().SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1305:*");
        builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().HasCommand("NarrowCapture").Delete().ReturnsMany(row => EF.Property<int>(row, "Revision"))
            .IncludeShadowProperties().SimulateImmediateWrites();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
    }

    [Fact]
    public async Task AcknowledgedOverridesWinOverOtherwiseEligibleMappedReturningBehavior()
    {
        var builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().HasCommand("ScriptedReturn").Delete().ReturnsMany(row => EF.Property<int>(row, "Revision"))
            .IncludeMappedProperties().SimulateImmediateWrites().RequireTestOverride("The test supplies an external receipt.");
        var (repository, _, store) = MappedStateTestHarness.Fixture(MappedReturnTestHarness.Compile(builder));
        Func<Task> execute = () => MappedStateTestHarness.Call<MutationResult<int>>(repository, "ScriptedReturnAsync");
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnScriptedReturnAsync", new MutationResult<int>([42]));
        (await MappedStateTestHarness.Call<MutationResult<int>>(repository, "ScriptedReturnAsync")).Items.Should().Equal(42);
        store.Count.Should().Be(3);
    }

    [Fact]
    public async Task RequestBoundResultProjectionLowersOriginalDesignerValuesNotNativeBufferLocals()
    {
        var builder = MappedReturnTestHarness.Builder();
        builder.Entity<MappedStateNote>().HasCommand("BoundReturn").Where<MappedStateRequest>((row, request) => row.Id == request.Id)
            .Update(setters => setters.SetProperty(row => row.Counter, row => row.Counter + 1))
            .ReturnsMany<string, MappedStateRequest>((row, request) => request.Title + ":" + EF.Property<int>(row, "Revision"))
            .IncludeMappedProperties().SimulateImmediateWrites();
        var (repository, _, _) = MappedStateTestHarness.Fixture(MappedReturnTestHarness.Compile(builder));
        (await MappedStateTestHarness.Call<MutationResult<string>>(repository, "BoundReturnAsync", new MappedStateRequest(1, "receipt")))
            .Items.Should().Equal("receipt:4");
    }
}
