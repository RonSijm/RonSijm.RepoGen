using System.Text.Json.Nodes;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.CompositeCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionGenerationTests
{
    private static readonly string[] MatchingProperties = ["Code", "Language"];
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    public void BothShapesCompileInAllLayoutsAndExposeTheMatchingContract(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context();
        var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(CompositeCollectionContext).Assembly, design: design);
        var collections = model.Entities.Single(entity => entity.EntityName == nameof(CompositeCollectionParent)).Commands.SelectMany(command => command.Collections!);
        collections.Should().OnlyContain(collection => collection.MatchingKeyProperties!.SequenceEqual(MatchingProperties));
        collections.Should().OnlyContain(collection => collection.ChildKeySource.Contains("RepositoryCollectionKeys.Require", StringComparison.Ordinal));
        var manifest = JsonNode.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents)!;
        var commands = manifest["entities"]!.AsArray().Single(entity => entity!["entity"]!.GetValue<string>().EndsWith("." + nameof(CompositeCollectionParent), StringComparison.Ordinal))!["commands"]!.AsArray();
        commands.Should().HaveCount(4);
        foreach (var command in commands)
        {
            var key = command!["collections"]![0]!["compositeMatchingKey"]!;
            key["properties"]!.AsArray().Select(value => value!.GetValue<string>()).Should().Equal("Code", "Language");
            key["equality"]!.GetValue<string>().Should().Be("DefaultClrValueTuple");
            key["nullComponents"]!.GetValue<string>().Should().Be("RejectBeforeMutation");
            key["mutable"]!.GetValue<bool>().Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("global")]
    [InlineData("subset")]
    public void AGlobalOrStrictSubsetUniqueConstraintAlsoProvesTheCombinationUnique(string scenario)
    {
        using var context = Context(scenario);
        var model = RepositoryModelFactory.Create(context, typeof(CompositeCollectionContext).Assembly, design: Builder().Build());
        model.Entities.Single(entity => entity.EntityName == nameof(CompositeCollectionParent)).Commands.Should().OnlyContain(command => command.InMemoryCommand!.Supported);
    }

    [Theory]
    [InlineData("nonunique")]
    [InlineData("filtered")]
    [InlineData("nullable")]
    [InlineData("generated")]
    [InlineData("extra")]
    public void MetadataMustProveAllSelectedRowsAreUnambiguouslyMatchable(string scenario)
    {
        using var context = Context(scenario);
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*matching*");
    }

    [Fact]
    public void ScalarDefaultsDoNotEmitCompositeInfrastructureAndFixtureOptOutDoesNotChangeProduction()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(simulate: false, strict: false).Build());
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("ReconcileAsync");
        Action strict = () => RepositoryGenerator.Generate(context, Builder(simulate: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Graph reconciliation*");
        using var scalarContext = MemoryCollectionTestHarness.Context();
        var scalar = RepositoryGenerator.Generate(scalarContext, new MemoryCollectionRepositoryDesigner());
        scalar.Files.Select(file => file.Contents).Should().OnlyContain(source => !source.Contains("RepositoryCollectionKeys", StringComparison.Ordinal) && !source.Contains("compositeMatchingKey", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderConversionRemainsAnExplicitFixtureBoundary()
    {
        using var context = Context("converted");
        Action strict = () => RepositoryGenerator.Generate(context, Builder().Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Custom*conversion*");
        var bundle = RepositoryGenerator.Generate(context, Builder(strict: false).Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }
}
