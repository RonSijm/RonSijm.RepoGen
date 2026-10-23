using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AutoIncludeReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void AllLayoutsAndBothStoreShapesCompileWithExplicitLoadingPreserved(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, builder.Build());
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        source.Should().Contain("EntityFrameworkQueryableExtensions.IgnoreAutoIncludes(")
            .And.Contain("ThenInclude").And.Contain("Books");
        var memory = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        memory.Should().NotContain("EntityFrameworkCore").And.NotContain("IgnoreAutoIncludes(");
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var queries = manifest.RootElement.GetProperty("entities").EnumerateArray().SelectMany(entity => entity.GetProperty("queries").EnumerateArray());
        queries.Should().OnlyContain(query => query.GetProperty("ignoreAutoIncludes").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadingOptionChangesNoServiceContractsOrApiSurface(bool mapped)
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, ignore: false, strict: false).Build());
        enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Contracts).Contents
            .Should().Be(disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Contracts).Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("ignoreAutoIncludes");
        disabled.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Should().OnlyContain(file => !file.Contents.Contains("IgnoreAutoIncludes(", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultsAndTrueThenFalseRemainByteIdenticalAndDoNotAffectSiblingQueries()
    {
        using var context = Context(); var builder = Builder(ignore: false, strict: false);
        var entity = builder.Entity<NavigationReadLibrary>();
        var toggle = entity.HasQuery("Toggle"); toggle.Select(row => row.Id).List();
        var before = RepositoryGenerator.Generate(context, builder.Build());
        toggle.IgnoreAutoIncludes().IgnoreAutoIncludes(false);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(before.Files);
        toggle.IgnoreAutoIncludes();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        var queries = model.Entities.Single(item => item.EntityName == nameof(NavigationReadLibrary)).Queries;
        queries.Single(query => query.MethodName == "ToggleAsync").InMemoryQuery!.Supported.Should().BeTrue();
        queries.Where(query => query.MethodName != "ToggleAsync").Should().OnlyContain(query => !query.IgnoreAutoIncludes && !query.InMemoryQuery!.Supported);
    }

    [Fact]
    public void ProductionPolicyDoesNotRequireTheFixtureBackend()
    {
        using var context = Context(); var builder = Builder(backend: false);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        SavedResultTestHarness.CompileProduction(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Repositories).Contents.Should().Contain("IgnoreAutoIncludes(");
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.SelectMany(entity => entity.Queries).Should().OnlyContain(query => query.InMemoryQuery == null);
    }
}
