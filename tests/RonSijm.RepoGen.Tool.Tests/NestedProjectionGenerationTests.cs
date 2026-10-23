using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NestedProjectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void NestedDataCompilesInEveryLayoutAndBothPublicStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("new global::RonSijm.RepoGen.Tool.Tests.NestedProjectionCard")
            .And.Contain("new global::RonSijm.RepoGen.Tool.Tests.NestedProjectionDetail")
            .And.NotContain("global::RonSijm.RepoGen.Tool.Tests.NestedProjectionCalls.").And.NotContain("EntityFrameworkCore");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents
            .Should().Contain("ExplicitNestedDtoAndMaterializedCollectionConstruction").And.Contain("ExactClosedMethodOverloadAndFixedCallShape")
            .And.Contain("NotProviderEquivalence");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NestedConsentCannotChangeProductionApiOrContracts(bool mapped)
    {
        using var context = MemoryWriteTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, translations: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(off.CreateApiSnapshot().ToJson());
    }

    [Fact]
    public void UnusedOrDisabledRecipesEmitNothingAndNeverInvokeDataCode()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(translations: false, strict: false);
        var baseline = RepositoryGenerator.Generate(context, builder.Build());
        builder.HasInMemoryProjectionTranslation<int, NestedProjectionPoison>("Poison", id => ProjectionNoteCalls.Echo<NestedProjectionPoison>(id),
            id => new NestedProjectionPoison(id), allowNestedData: true);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(baseline.Files);
        var disabled = RepositoryGenerator.Generate(context, Builder(queries: false, commands: false).Build());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("expressionTranslations");
    }

    [Fact]
    public void CommandConsentRemainsSeparateFromNestedReadRegistration()
    {
        using var context = MemoryWriteTestHarness.Context(); var builder = Builder(commandConsent: false, strict: false);
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single().Queries.Should().OnlyContain(query => query.InMemoryQuery!.Supported);
        model.Entities.Single().Commands.Where(command => command.MethodName != "StageRenameAsync")
            .Should().OnlyContain(command => !command.InMemoryCommand!.Supported);
        builder.Configure(options => options.RequirePortableTestCommands());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
