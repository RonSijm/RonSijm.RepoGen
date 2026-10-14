using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.LinkWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteGenerationTests
{
    private static readonly string[] TargetForeignKeys = ["TargetRegion", "TargetCode"];
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void BothShapesCompileInAllLayoutsWithInspectableJoinBoundaries(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(LinkWriteContext).Assembly, design: design);
        var links = model.Entities.Single(entity => entity.EntityName == nameof(LinkWriteOwner)).Commands.SelectMany(command => command.Collections ?? []);
        links.Should().HaveCount(5).And.OnlyContain(collection => collection.Link != null);
        links.Should().OnlyContain(collection => collection.Link!.TargetForeignKeys.SequenceEqual(TargetForeignKeys));
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Should().Contain("ReconcileCollection<").And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
        var production = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        production.Should().Contain("ApplyLinksAsync").And.Contain("ValidateSaveBoundary");
        var receipt = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        receipt.Should().Contain("linkReconciliation").And.Contain("ExistingReferencesOnlyNeverCreatedUpdatedOrDeleted")
            .And.Contain("DatabaseEnforcedNotFixtureSimulated").And.Contain("NotImplicitlyInjected");
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("partial")]
    [InlineData("reordered")]
    [InlineData("payload")]
    [InlineData("owner")]
    [InlineData("changeReference")]
    [InlineData("ownerMapping")]
    [InlineData("navigationMapping")]
    public void NewReferencePermissionCannotBecomeABlanketForeignKeyOrGraphWritePermission(string invalid)
    {
        using var context = Context(); var builder = Builder(); var owner = builder.Entity<LinkWriteOwner>();
        var command = owner.HasCommand("Unsafe").Where<LinkWriteRequest>((row, request) => row.Id == request.OwnerId && row.TenantId == request.TenantId);
        switch (invalid)
        {
            case "ordinary": command.ReconcileCollection<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!, row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code), links => links.Add(item => new LinkWriteRow { TargetRegion = item.Region, TargetCode = item.Code })).Save(); break;
            case "partial": command.ReconcileLinks<LinkWriteRow, LinkWriteItem, string, LinkWriteRequest>(row => row.Links, request => request.Items!, row => row.TargetCode, item => item.Code, _ => { }).Save(); break;
            case "payload": command.ReconcileLinks<LinkWriteRow, LinkWriteItem, string, LinkWriteRequest>(row => row.Links, request => request.Items!, row => row.Note, item => item.Note, _ => { }).Save(); break;
            case "owner": command.ReconcileLinks<LinkWriteRow, LinkWriteItem, int, LinkWriteRequest>(row => row.Links, request => request.Items!, row => row.OwnerId, _ => 1, _ => { }).Save(); break;
            case "reordered": command.ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!, row => new ValueTuple<string, string>(row.TargetCode, row.TargetRegion), item => new ValueTuple<string, string>(item.Code, item.Region), _ => { }).Save(); break;
            default:
                command.ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!, row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code), links =>
                {
                    if (invalid == "changeReference") links.AllowWrite(row => row.TargetCode).Update(setters => setters.SetProperty(row => row.TargetCode, (row, item) => item.Code));
                    if (invalid == "ownerMapping") links.AllowWrite(row => row.OwnerId).Add(item => new LinkWriteRow { OwnerId = 99, TargetRegion = item.Region, TargetCode = item.Code });
                    if (invalid == "navigationMapping") links.Add(item => new LinkWriteRow { TargetRegion = item.Region, TargetCode = item.Code, Target = new LinkWriteTarget { Code = item.Code, Region = item.Region } });
                }).Save(); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid switch
        { "ordinary" => "RGD2801:*", "changeReference" or "ownerMapping" or "navigationMapping" => "RGD2802:*", _ => "RGD3901:*" });
    }

    [Fact]
    public void CollectionSimulationIsAnIndependentOptInAndNeverChangesProduction()
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOptionalOrThirdReferenceIsNotAnOrdinaryTwoEndpointLink(bool third)
    {
        var options = new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite("Data Source=:memory:").Options;
        using LinkWriteContext context = third ? new LinkWriteThirdForeignKeyContext(options) : new LinkWriteOptionalContext(options);
        var builder = new RepositoryModelBuilder().Configure(settings => settings.UseSimpleDefaults().UseClosedRepositories(Namespace + ".Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<LinkWriteOwner>().HasCommand("Change").Where<LinkWriteRequest>((row, request) => row.Id == request.OwnerId)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code), _ => { }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3901:*two required, disjoint*");
    }
}
