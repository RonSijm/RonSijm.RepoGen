using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OverlappingReferenceTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceGuardTests
{
    [Theory]
    [InlineData("incomplete")]
    [InlineData("update")]
    [InlineData("owner")]
    [InlineData("graph")]
    [InlineData("reset")]
    public void SharedPermissionRetainsCompleteMapsImmutableReferencesAndScalarOnlyCreation(string invalid)
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<OverlappingReferenceOwner>().HasCommand("Unsafe")
            .Where<OverlappingReferenceRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
            .ReconcileLinks<OverlappingReferenceLink, OverlappingReferenceItem, (int, int), OverlappingReferenceRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                {
                    links.AllowSharedReferenceColumns().TargetReference(row => row.Target).AdditionalReference(row => row.Actor)
                        .AdditionalReference(row => row.Reviewer).AdditionalReference(row => row.Source);
                    if (invalid == "reset") links.AllowSharedReferenceColumns(false);
                    if (invalid == "incomplete") links.Add(item => new OverlappingReferenceLink { Scope = item.Scope, TargetId = item.TargetId, ActorCode = item.ActorCode, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId });
                    if (invalid == "owner") links.Add(item => new OverlappingReferenceLink { Scope = item.Scope, TargetId = item.TargetId, Zone = item.Zone, OwnerId = 1, ActorCode = item.ActorCode, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId });
                    if (invalid == "graph") links.Add(item => new OverlappingReferenceLink { Scope = item.Scope, TargetId = item.TargetId, Zone = item.Zone, ActorCode = item.ActorCode, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId, Source = new OverlappingReferenceSource() });
                    if (invalid == "update") links.AllowWrite(row => row.Zone).Update(setters => setters.SetProperty(row => row.Zone, (row, item) => item.Zone));
                }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid switch { "incomplete" => "RGD4202:*", "reset" => "RGD3902:*", _ => "RGD2802:*" });
    }

    [Fact]
    public void ExplicitPropertyProtectionStillAppliesToSharedCreationColumns()
    {
        using var context = Context(); var builder = Builder(); builder.Entity<OverlappingReferenceLink>().ProtectWrite(row => row.Zone);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
    }

    [Fact]
    public void ARequiredAdditionalReferenceMustStillHaveANonOwnerComponent()
    {
        using var context = new OverlappingReferenceOwnedColumnsContext(new DbContextOptionsBuilder<OverlappingReferenceContext>().UseSqlite("Data Source=:memory:").Options);
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3902:*independent*");
    }

    [Fact]
    public void RequiredDeclarationsDoNotAdmitOptionalOverlappingReferences()
    {
        using var context = new OverlappingReferenceOptionalContext(new DbContextOptionsBuilder<OverlappingReferenceContext>().UseSqlite("Data Source=:memory:").Options);
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4201:*");
    }
}
