using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicSavedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicSavedGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void FreshAtomicResultsCompileInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        source.Should().Contain("ExecuteWithResultQueryAsync").And.Contain("__repoGenSavedEntity.Id");
        var testing = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        testing.Should().Contain("ExecuteInsertOrUpdateWithSavedResultAsync").And.Contain("var __repoGenSnapshot = __repoGenSavedSnapshot;");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should()
            .Contain("atomicSavedResultQuery").And.Contain("PreparedRootOverlayWithSavedOnlyRelatedRowsBeforeAtomicCommit");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        ImmediateTestHarness.CompileProduction(context, builder);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AtomicGraphsRequireSeparateReadConsent(bool navigations, bool queries)
    {
        using var context = MemoryCollectionTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(navigations, queries).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void DisablingTestBackendLeavesProductionAndContractsUnchanged()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
        {
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        }
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Stage")]
    [InlineData("Reload")]
    public void AtomicSavedResultsRetainLifecycleGuards(string mode)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = Builder();
        var command = builder.Entity<MemoryCollectionChild>().HasCommand("Invalid")
            .Create<AtomicSavedRequest>(request => new MemoryCollectionChild { ParentId = request.ParentId, Key = request.Key, Text = request.Text })
            .AllowWrite(row => row.ParentId).InsertIfAbsent(row => new { row.ParentId, row.Key }).ReturnsSaved(row => row.Text);
        if (mode == "Save")
        {
            command.Save();
        }
        if (mode == "Stage")
        {
            command.Stage();
        }
        if (mode == "Reload")
        {
            command.ReloadAfterSave();
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3701:*");
    }
}
