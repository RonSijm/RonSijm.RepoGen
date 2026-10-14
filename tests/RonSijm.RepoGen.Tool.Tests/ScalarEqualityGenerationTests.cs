using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScalarEqualityTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void ExplicitScalarComparisonsCompileWithoutEfDependenciesInEveryLayout(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build(); var bundle = RepositoryGenerator.Generate(context, design, layout);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        assembly.GetReferencedAssemblies().Should().NotContain(reference => reference.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        var model = RepositoryModelFactory.Create(context, typeof(ScalarEqualityRow).Assembly, design: design);
        model.Entities.Single().Commands.Should().OnlyContain(command => command.InMemoryCommand!.Supported);
        model.Entities.Single().InMemoryStore!.ValueEqualities!.Select(policy => policy.PropertyName).Should().Equal("Label", "Revision");
        RepositoryGeneratedTypes.Names(model).Should().Contain(Namespace + ".Testing.__RepoGenScalarEqualityRowValueEqualities");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("TrackedFixtureStateAndVersionsOnly").And.Contain("OrdinaryStructuralEqualityStillRequired");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnablingPoliciesCannotChangeProductionContractsOrCacheFingerprints(bool mapped)
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, policies: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        Action strict = () => RepositoryGenerator.Generate(context, Builder(mapped, policies: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("*RGD3402*Custom scalar conversions/comparers*");
    }

    [Fact]
    public void RemovingPoliciesOrDisablingTheCommandBackendLeavesNoEqualityHelpersOrReceipts()
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<ScalarEqualityRow>().WithoutInMemoryValueEquality(row => row.Label).WithoutInMemoryValueEquality(row => row.Revision);
        builder.Configure(options => options.RequirePortableTestCommands(false));
        RepositoryGenerator.Generate(context, builder.Build()).Files.Select(file => file.Contents).Should().Equal(RepositoryGenerator.Generate(context, Builder(policies: false, strict: false).Build()).Files.Select(file => file.Contents));
        RepositoryGenerator.Generate(context, Builder(commands: false).Build()).Files.Select(file => file.Contents).Should().Equal(RepositoryGenerator.Generate(context, Builder(policies: false, commands: false).Build()).Files.Select(file => file.Contents));
    }
}
