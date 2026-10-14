using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SharedLinkTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void BothStoreShapesCompileInAllLayoutsWithTypedOwnerGuardsAndReceipts(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build(); var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(SharedLinkContext).Assembly, design: design);
        var collections = model.Entities.Single(entity => entity.EntityName == nameof(SharedLinkOwner)).Commands.SelectMany(command => command.Collections ?? []).ToArray();
        collections.Should().HaveCount(6);
        foreach (var collection in collections)
        {
            var column = collection.Link!.SharedReferenceColumns.Should().ContainSingle().Which;
            column.PropertyName.Should().Be(nameof(SharedLinkRow.Scope)); column.OwnerPropertyName.Should().Be(nameof(SharedLinkOwner.SpaceId));
            column.ReferenceKeyIndex.Should().Be(0);
        }
        var production = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        production.Contains("matchesOwner:", StringComparison.Ordinal).Should().BeTrue();
        production.Contains(">(\"SpaceId\").CurrentValue, __repoGenReferenceKey.Item1", StringComparison.Ordinal).Should().BeTrue();
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Should().Contain("matchesOwner:").And.Contain("__repoGenReferenceOwner.SpaceId, __repoGenReferenceKey.Item1").And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("context.Entry(");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("sharedReferenceColumns")
            .And.Contain("CompleteTargetKeyMustAgreeWithSelectedOwner").And.Contain("AllSuppliedKeysBeforeCollectionLoadingOrMutation");
    }

    [Fact]
    public void OverlappingColumnsRequireExplicitPermissionEvenWithoutFixtureGeneration()
    {
        using var context = Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(shared: false, backend: false).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3902:*AllowSharedReferenceColumns*");
    }

    [Theory]
    [InlineData(true, "key")]
    [InlineData(false, "key")]
    [InlineData(true, "owner")]
    [InlineData(false, "owner")]
    public void GuardLocalsCannotCollideWithCommonApplicationParameterNames(bool mapped, string parameter)
    {
        using var context = Context(); var builder = Builder(mapped);
        var command = builder.Entity<SharedLinkOwner>().HasCommand("NamedInput");
        if (parameter == "key") command.Where<SharedLinkRequest>((row, key) => row.SpaceId == key.SpaceId && row.Id == key.OwnerId)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, key => key.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns());
        else command.Where<SharedLinkRequest>((row, owner) => row.SpaceId == owner.SpaceId && row.Id == owner.OwnerId)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, owner => owner.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns());
        command.Save().SimulateCollectionWrites();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        SavedResultTestHarness.CompileProduction(context, builder.Build());
    }

    [Fact]
    public void FixtureSimulationRemainsIndependentOfProductionAndContracts()
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(simulate: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(disabled);
        Action strict = () => RepositoryGenerator.Generate(context, Builder(simulate: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Graph reconciliation*");
    }

    [Fact]
    public void AnInactiveSharedColumnOptionChangesNoExistingDisjointOutput()
    {
        RepositoryDesign Design(bool enabled)
        {
            var builder = LinkWriteTestHarness.Builder();
            builder.Entity<LinkWriteOwner>().HasCommand("Probe").Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
                .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                    links => links.AllowSharedReferenceColumns(enabled)).Save().SimulateCollectionWrites();
            return builder.Build();
        }
        using var context = LinkWriteTestHarness.Context(); var ordinary = RepositoryGenerator.Generate(context, Design(false));
        var enabled = RepositoryGenerator.Generate(context, Design(true));
        enabled.Files.Select(file => (file.Kind, file.Name, file.Contents)).Should().Equal(ordinary.Files.Select(file => (file.Kind, file.Name, file.Contents)));
    }

    [Fact]
    public void TheOptionCanBeDisabledAgainAndDoesNotApplyToOrdinaryCollections()
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<SharedLinkOwner>().HasCommand("Disabled").Where<SharedLinkRequest>((row, request) => row.Id == request.OwnerId)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns().AllowSharedReferenceColumns(false)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3902:*");
        Action ordinary = () => new RepositoryModelBuilder().Entity<SharedLinkOwner>().HasCommand("Ordinary")
            .ReconcileCollection<SharedLinkRow, SharedLinkItem, int, SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => row.TargetId, item => item.TargetId, children => children.AllowSharedReferenceColumns());
        ordinary.Should().Throw<InvalidOperationException>().WithMessage("RGD3902:*only supported*");
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("reordered")]
    [InlineData("sharedUpdate")]
    [InlineData("ownerMapping")]
    public void SharedReferencesDoNotWeakenCompleteMatchingOrForeignKeyImmutability(string invalid)
    {
        using var context = Context(); var builder = Builder(); var command = builder.Entity<SharedLinkOwner>().HasCommand("Unsafe")
            .Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId);
        if (invalid == "partial")
            command.ReconcileLinks<SharedLinkRow, SharedLinkItem, int, SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => row.TargetId, item => item.TargetId, links => links.AllowSharedReferenceColumns()).Save();
        else if (invalid == "reordered")
            command.ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.TargetId, row.Scope), item => new ValueTuple<int, int>(item.TargetId, item.Scope),
                links => links.AllowSharedReferenceColumns()).Save();
        else
            command.ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId),
                item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                {
                    links.AllowSharedReferenceColumns();
                    if (invalid == "sharedUpdate") links.AllowWrite(row => row.Scope).Update(setters => setters.SetProperty(row => row.Scope, (row, item) => item.Scope));
                    if (invalid == "ownerMapping") links.AllowWrite(row => row.OwnerId).Add(item => new SharedLinkRow { Scope = item.Scope, TargetId = item.TargetId, OwnerId = 99 });
                }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid is "partial" or "reordered" ? "RGD3901:*" : "RGD2802:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSharingDoesNotEnableOptionalOrThirdForeignKeys(bool third)
    {
        var options = new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite("Data Source=:memory:").Options;
        using LinkWriteContext context = third ? new LinkWriteThirdForeignKeyContext(options) : new LinkWriteOptionalContext(options);
        var builder = new RepositoryModelBuilder().Configure(settings => settings.UseSimpleDefaults().UseClosedRepositories(Namespace + ".Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<LinkWriteOwner>().HasCommand("Change").Where<LinkWriteRequest>((row, request) => row.Id == request.OwnerId)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                links => links.AllowSharedReferenceColumns()).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3901:*");
    }
}
