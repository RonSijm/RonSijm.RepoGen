using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OverlappingReferenceTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void ExplicitRequiredRolesCompileWithAliasedReorderedAndSharedKeys(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var fixture = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        fixture.Should().Contain("matchesCreatedOwner:").And.Contain("__repoGenReferenceOwner.ZoneId").And.NotContain("Microsoft.EntityFrameworkCore");
        var receipt = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        receipt.RootElement.ToString().Should().Contain("NewLinkInitializerOnceBeforeMatchedChildVersionsOrMutation");
        var model = RepositoryModelFactory.Create(context, typeof(OverlappingReferenceContext).Assembly, design: design);
        var link = model.Entities.Single(entity => entity.EntityName == nameof(OverlappingReferenceOwner)).Commands.First(command => command.Collections?.Count > 0).Collections![0].Link!;
        link.AdditionalReferences!.Select(reference => reference.NavigationName).Should().Equal("Actor", "Reviewer", "Source");
        link.AdditionalReferences![0].SharedColumns.Should().ContainSingle().Which.SharedWith.Should().Equal("Additional:Reviewer", "Owner", "Target:Target");
        link.AdditionalReferences![2].SharedColumns.Should().ContainSingle().Which.OwnerPropertyName.Should().Be(nameof(OverlappingReferenceOwner.ZoneId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixtureConsentIsIndependentAndDoesNotChangeProductionOrContractShapes(bool mapped)
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(disabled);
        Action missingConsent = () => RepositoryGenerator.Generate(context, Builder(mapped, simulate: false).Build());
        missingConsent.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void SharedAdditionalColumnsNeverBecomeImplicitPermission()
    {
        using var context = Context();
        Action disabled = () => RepositoryGenerator.Generate(context, Builder(shared: false).Build());
        disabled.Should().Throw<InvalidOperationException>().WithMessage("RGD3902:*");
    }

    [Fact]
    public void OperationsWithoutAddDoNotEmitUnusedCreationGuards()
    {
        using var context = Context(); var builder = Builder(); var owner = builder.Entity<OverlappingReferenceOwner>();
        foreach (var command in builder.Build().Entities.Single(entity => entity.EntityType == typeof(OverlappingReferenceOwner)).Commands.ToArray())
            owner.ExcludeCommand(command.MethodName);
        owner.HasCommand("NoAdd").Where<OverlappingReferenceRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
            .ReconcileLinks<OverlappingReferenceLink, OverlappingReferenceItem, (int, int), OverlappingReferenceRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                    links.AllowSharedReferenceColumns().TargetReference(row => row.Target).AdditionalReference(row => row.Actor)
                        .AdditionalReference(row => row.Reviewer).AdditionalReference(row => row.Source).MaximumItems(4)).Save().SimulateCollectionWrites();
        var design = builder.Build(); var bundle = RepositoryGenerator.Generate(context, design);
        string.Join("\n", bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.TestDoubles).Select(file => file.Contents))
            .Should().NotContain("matchesCreatedOwner:");
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
    }
}
