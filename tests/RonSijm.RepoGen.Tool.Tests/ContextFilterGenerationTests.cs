using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ContextFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void BoundFiltersCompileWithoutEvaluatingContextGettersInEveryLayout(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); ImmediateTestHarness.CompileProduction(context, builder);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("var __repoGenModelFilterContextKey = input.Key;")
            .And.Contain("Fixture.For(__repoGenModelFilterContextKey)").And.NotContain("EntityFrameworkCore").And.NotContain("RuntimeKey");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("\"ContextBindings\"").And.Contain("contextKey.Tenant").And.Contain("contextKey.IncludeDeleted").And.Contain("contextKey.FolderId");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BindingDoesNotChangeProductionContractsCacheOrApiAndDisabledBackendsAreByteIdentical(bool mapped)
    {
        using var context = Context();
        var on = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, bound: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(on.CreateApiSnapshot().ToJson());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build());
        RepositoryGenerator.Generate(context, Builder(mapped, bound: false, backend: false).Build()).Files.Should().BeEquivalentTo(disabled.Files);
    }

    [Fact]
    public void UnusedBindingsAndIgnoredFiltersDoNotAddGeneratedLocalsOrReceipts()
    {
        using var context = Context(); var builder = Base();
        builder.Entity<ModelFilterRow>().HasQuery("All").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .IgnoreQueryFilters().Select(row => row.Id).List();
        var on = RepositoryGenerator.Generate(context, builder.Build());
        var offBuilder = Base(bound: false);
        offBuilder.Entity<ModelFilterRow>().HasQuery("All").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .IgnoreQueryFilters().Select(row => row.Id).List();
        RepositoryGenerator.Generate(context, offBuilder.Build()).Files.Should().BeEquivalentTo(on.Files);
        using var plain = ModelFilterTestHarness.Context();
        // A binding is never a fallback for an unrelated context or arbitrary captured services.
        Action invalidContext = () => RepositoryGenerator.Generate(plain, Builder().Build());
        invalidContext.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
    }
}
