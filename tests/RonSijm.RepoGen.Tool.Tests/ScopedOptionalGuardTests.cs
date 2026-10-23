using FluentAssertions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScopedOptionalTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalGuardTests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("reset")]
    [InlineData("independentMap")]
    [InlineData("sharedMap")]
    [InlineData("update")]
    [InlineData("requiredDeclaration")]
    public void SharedOptionalDeclarationsDoNotRelaxRoleMappingOrWriteRules(string invalid)
    {
        using var context = Context(); var builder = Builder(shared: invalid != "permission", change: links =>
        {
            if (invalid == "reset") links.AllowSharedReferenceColumns(false);
            if (invalid == "independentMap") links.Add(item => new ScopedOptionalRow { Scope = item.Scope, Zone = item.Zone, TargetId = item.TargetId, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = item.ReviewerRegion, OriginId = item.OriginId });
            if (invalid == "sharedMap") links.Add(item => new ScopedOptionalRow { Scope = item.Scope, TargetId = item.TargetId, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = item.ReviewerRegion, ReviewerCode = item.ReviewerCode, OriginId = item.OriginId });
            if (invalid == "update") links.AllowWrite(row => row.OriginId).Update(setters => setters.SetProperty(row => row.OriginId, (row, item) => item.OriginId));
            if (invalid == "requiredDeclaration") links.WithoutAdditionalReference(row => row.Reviewer).AdditionalReference(row => row.Reviewer);
        });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid switch
        {
            "permission" or "reset" => "RGD3902:*", "independentMap" or "sharedMap" => "RGD4202:*", "update" => "RGD2802:*", _ => "RGD4201:*"
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentComponentsMustRemainNullableAndNonGenerated(bool generated)
    {
        using ScopedOptionalContext context = generated ? new ScopedOptionalGeneratedContext(Options()) : new ScopedOptionalMixedContext(Options());
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4301:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonOwnerOptionalOverlapsStayUnsupportedEvenWithSharedPermission(bool enabled)
    {
        using var context = new OptionalReferenceOverlapContext(OptionalReferenceTestHarness.Options());
        Action generate = () => RepositoryGenerator.Generate(context, OptionalReferenceTestHarness.Builder(change: links => links.AllowSharedReferenceColumns(enabled)).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4201:*");
    }

    [Fact]
    public void ExplicitProtectionStillAppliesToOwnerSharedOptionalCreation()
    {
        using var context = Context(); var builder = Builder(); builder.Entity<ScopedOptionalRow>().ProtectWrite(row => row.Zone);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
    }
}
