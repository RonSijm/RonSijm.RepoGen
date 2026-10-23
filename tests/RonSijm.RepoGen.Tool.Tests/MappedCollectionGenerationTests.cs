using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryLayoutEmitsTypedMappedParticipantStateAndSeparateCoverage(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, Builder().Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        syntax.DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.IdentifierToken)
            .Select(token => token.ValueText).Should().NotContain("EF").And.NotContain("EntityFrameworkCore");
        source.Should().Contain("ReconcileCollection<");
        syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Select(node => node.Type.ToString())
            .Should().NotContain("global::RonSijm.RepoGen.Tool.Tests.MappedCollectionParent")
            .And.NotContain("global::RonSijm.RepoGen.Tool.Tests.MappedCollectionChild");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("mappedScalarState")
            .And.Contain("CompleteMapInMemoryWrites(useMappedPropertyState: true)OnEachMappedParticipant")
            .And.Contain("FullStructuralReadWriteReadBeforeAnyStoreCommits");
    }

    [Fact]
    public void FixturePermissionsDoNotChangeProductionContractsApiOrSources()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("mappedScalarState");
    }

    [Fact]
    public async Task AutomaticReadsAreIndependentOfMappedScalarCollectionWrites()
    {
        var (parent, _, data) = Fixture(Compile(Builder(queries: false)));
        (await Call<RonSijm.RepoGen.Abstractions.CommandResult<int>>(parent, "Replace", Request())).Value.Should().Be(4);
        ChildStore(data).Find(20)!.Revision.Should().Be(1);
    }

    [Fact]
    public async Task StateBindingsAloneDoNotGrantCollectionSimulationEvenWhenTheStoreIsEmpty()
    {
        using var context = Context();
        var builder = Builder(simulate: false, strict: false);
        var (parent, _, data) = Fixture(Compile(builder));
        ChildStore(data).Clear();
        Func<Task> execute = () => Call<RonSijm.RepoGen.Abstractions.CommandResult<int>>(parent, "Replace", Request());
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Graph reconciliation writes require*");
    }

    [Theory]
    [InlineData("ParentId")]
    [InlineData("Code")]
    [InlineData("Id")]
    public void ScalarStateCannotReplaceOwnershipMatchingOrFixtureIdentity(string property)
    {
        using var context = Context();
        var builder = Builder();
        var child = builder.Entity<MappedCollectionChild>();
        if (property == "Code")
        {
            child.MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild { Id = row.Id, ParentId = row.ParentId, Text = row.Text, Counter = row.Counter })
                .MapInMemoryProperty<MappedCollectionChildSeed, string>(property, row => row.Code);
        }
        else
        {
            if (property == "Id")
            {
                child.MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild { ParentId = row.ParentId, Code = row.Code, Text = row.Text, Counter = row.Counter })
                    .MapInMemoryProperty<MappedCollectionChildSeed, int>(property, row => row.Id);
            }
            else
            {
                child.MapInMemoryEntity<MappedCollectionChildSeed>(row => new MappedCollectionChild { Id = row.Id, Code = row.Code, Text = row.Text, Counter = row.Counter })
                    .MapInMemoryProperty<MappedCollectionChildSeed, int>(property, row => row.ParentId);
            }
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*Identity/ownership*");
    }
}
