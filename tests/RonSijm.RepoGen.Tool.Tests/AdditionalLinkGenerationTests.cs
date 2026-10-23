using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AdditionalLinkTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void NamedRolesCompileInEveryLayoutForEntityAndPublicMappedStores(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build(); var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(AdditionalLinkContext).Assembly, design: design);
        var links = model.Entities.Single(entity => entity.EntityName == nameof(AdditionalLinkOwner)).Commands.SelectMany(command => command.Collections ?? []).ToArray();
        links.Should().HaveCount(3);
        foreach (var link in links)
        {
            link.Link!.TargetNavigationName.Should().Be(nameof(AdditionalLinkRow.Target));
            link.Link.AdditionalReferences!.Select(reference => reference.NavigationName).Should().Equal(nameof(AdditionalLinkRow.Actor), nameof(AdditionalLinkRow.Source));
            link.Link.AdditionalReferences![0].ForeignKeys.Should().Equal(nameof(AdditionalLinkRow.ActorRegion), nameof(AdditionalLinkRow.ActorCode));
            link.Link.SharedReferenceColumns.Should().ContainSingle();
        }
        var receipt = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        receipt.Contains("EveryScalarComponentExplicitOnNewLinksOnly", StringComparison.Ordinal).Should().BeTrue();
        receipt.Contains("ImmutableOnMatchedLinksNeverWriteReferencedEntities", StringComparison.Ordinal).Should().BeTrue();
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public void AdditionalRolesAreExplicitAndFixtureSimulationIsIndependent()
    {
        using var context = Context();
        Action implicitRoles = () => RepositoryGenerator.Generate(context, Builder(roles: false).Build());
        implicitRoles.Should().Throw<InvalidOperationException>().WithMessage("RGD3901:*");
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(simulate: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(disabled);
        Action strict = () => RepositoryGenerator.Generate(context, Builder(simulate: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void RemovingANamedTargetPreservesTheOriginalTwoReferenceOutputExactly()
    {
        RepositoryDesign Design(bool removed)
        {
            var builder = LinkWriteTestHarness.Builder();
            builder.Entity<LinkWriteOwner>().HasCommand("Probe").Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
                .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code), links =>
                    {
                        if (removed) links.TargetReference(row => row.Target).WithoutTargetReference();
                    }).Save().SimulateCollectionWrites();
            return builder.Build();
        }
        using var context = LinkWriteTestHarness.Context();
        var normal = RepositoryGenerator.Generate(context, Design(false)); var removed = RepositoryGenerator.Generate(context, Design(true));
        removed.Files.Select(file => (file.Kind, file.Name, file.Contents)).Should().Equal(normal.Files.Select(file => (file.Kind, file.Name, file.Contents)));
    }

    [Theory]
    [InlineData("missingTarget")]
    [InlineData("missingRole")]
    [InlineData("ownerTarget")]
    [InlineData("sameRole")]
    [InlineData("incompleteMap")]
    [InlineData("updateReference")]
    [InlineData("graphMap")]
    public void ExplicitRolesNeverBecomeAnUnrestrictedForeignKeyOrGraphPermission(string invalid)
    {
        using var context = Context(); var builder = Builder(); var command = builder.Entity<AdditionalLinkOwner>().HasCommand("Unsafe")
            .Where<AdditionalLinkRequest>((row, request) => row.Scope == request.Scope && row.Id == request.OwnerId)
            .ReconcileLinks<AdditionalLinkRow, AdditionalLinkItem, (int, int), AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                {
                    links.AllowSharedReferenceColumns();
                    if (invalid != "missingTarget") links.TargetReference(row => row.Target);
                    if (invalid == "ownerTarget") links.TargetReference(row => row.Owner);
                    links.AdditionalReference(row => row.Actor);
                    if (invalid != "missingRole") links.AdditionalReference(row => row.Source);
                    if (invalid == "sameRole") links.AdditionalReference(row => row.Target);
                    if (invalid == "incompleteMap") links.Add(item => new AdditionalLinkRow { Scope = item.Scope, TargetId = item.TargetId, ActorRegion = item.ActorRegion, SourceId = item.SourceId });
                    if (invalid == "updateReference") links.AllowWrite(row => row.SourceId).Update(setters => setters.SetProperty(row => row.SourceId, (row, item) => item.SourceId));
                    if (invalid == "graphMap") links.Add(item => new AdditionalLinkRow { Scope = item.Scope, TargetId = item.TargetId, ActorRegion = item.ActorRegion, ActorCode = item.ActorCode, SourceId = item.SourceId, Source = new AdditionalLinkSource { Id = item.SourceId } });
                }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid switch { "incompleteMap" => "RGD4202:*", "updateReference" or "graphMap" => "RGD2802:*", _ => "RGD4201:*" });
    }

    [Fact]
    public void RemovingRolesRestoresDefaultBehaviorAndDuplicateOrNestedDeclarationsFailImmediately()
    {
        using var context = Context(); var builder = Builder(); var command = builder.Entity<AdditionalLinkOwner>().HasCommand("Removed")
            .Where<AdditionalLinkRequest>((row, request) => row.Id == request.OwnerId)
            .ReconcileLinks<AdditionalLinkRow, AdditionalLinkItem, (int, int), AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.TargetReference(row => row.Target).AdditionalReference(row => row.Actor).AdditionalReference(row => row.Source)
                    .WithoutAdditionalReference(row => row.Actor).WithoutAdditionalReference(row => row.Source).WithoutTargetReference()).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3901:*");
        Action duplicate = () => builder.Entity<AdditionalLinkOwner>().HasCommand("Duplicate")
            .ReconcileLinks<AdditionalLinkRow, AdditionalLinkItem, (int, int), AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AdditionalReference(row => row.Actor).AdditionalReference(row => row.Actor));
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD4201:*already declared*");
        Action nested = () => builder.Entity<AdditionalLinkOwner>().HasCommand("Nested")
            .ReconcileLinks<AdditionalLinkRow, AdditionalLinkItem, (int, int), AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AdditionalReference(row => row.Owner!.Links));
        nested.Should().Throw<ArgumentException>().WithMessage("RGD4201:*direct*");
        Action ordinary = () => builder.Entity<AdditionalLinkOwner>().HasCommand("Ordinary")
            .ReconcileCollection<AdditionalLinkRow, AdditionalLinkItem, int, AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                row => row.TargetId, item => item.TargetId, links => links.TargetReference(row => row.Target));
        ordinary.Should().Throw<InvalidOperationException>().WithMessage("RGD4201:*only supported*");
    }

    [Fact]
    public void NamedRequiredReferencesStillRejectImplicitOptionalRelationships()
    {
        var options = new DbContextOptionsBuilder<AdditionalLinkContext>().UseSqlite("Data Source=:memory:").Options;
        using var context = new AdditionalLinkOptionalContext(options);
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4201:*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitSharedPermissionAllowsAdditionalRolesUsingTheMatchingTargetsColumns(bool mapped)
    {
        using var context = new AdditionalLinkOverlapContext(new DbContextOptionsBuilder<AdditionalLinkContext>().UseSqlite("Data Source=:memory:").Options);
        var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(AdditionalLinkContext).Assembly, design: design);
        var source = model.Entities.Single(entity => entity.EntityName == nameof(AdditionalLinkOwner)).Commands.First(command => command.Collections?.Count > 0)
            .Collections![0].Link!.AdditionalReferences!.Single(reference => reference.NavigationName == nameof(AdditionalLinkRow.Source));
        source.SharedColumns.Should().ContainSingle().Which.SharedWith.Should().Equal("Target:Target");
    }

    [Fact]
    public void ACreateOnlyReferenceDeclarationDoesNotBypassExplicitPropertyProtection()
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<AdditionalLinkRow>().ProtectWrite(row => row.SourceId);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
    }
}
