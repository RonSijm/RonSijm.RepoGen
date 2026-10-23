using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NativeFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void FilteredNativeReturningCompilesInEveryLayoutAndBothStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout); ScriptedTestDoubleTestHarness.Compile(bundle);
        ImmediateTestHarness.CompileProduction(context, builder);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("EntityFrameworkCore");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("ScalarNativeReturningSavedSelectionNoFreshResultFilterGraphOrUpsert")
            .And.Contain("MatchedPostUpdateValuesNoPostMutationFilter").And.Contain("MatchedPreDeleteValuesNoPostMutationFilter");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SeparateOptInsDoNotChangeProductionContractsOrApiAndDisabledBackendIsByteStable(bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, filter: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build()).Files.Should().BeEquivalentTo(
            RepositoryGenerator.Generate(context, Builder(mapped, backend: false, filter: false).Build()).Files);
    }

    [Fact]
    public void ScalarReturningNeedsNoAutomaticReadBackend()
    {
        using var context = ModelWriteFilterTestHarness.Context();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(queries: false).Build()));
    }
}
