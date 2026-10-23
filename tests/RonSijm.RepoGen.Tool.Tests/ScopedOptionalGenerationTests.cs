using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScopedOptionalTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void NullableIndependentComponentsCompileSeparatelyFromSharedOwnership(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build(); var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(ScopedOptionalContext).Assembly, design: design);
        var references = model.Entities.Single(entity => entity.EntityName == nameof(ScopedOptionalOwner)).Commands.First(command => command.Collections?.Count > 0).Collections![0].Link!.AdditionalReferences!;
        references.Single(reference => reference.NavigationName == nameof(ScopedOptionalRow.Reviewer)).OptionalPresenceColumns.Should().Equal(nameof(ScopedOptionalRow.ReviewerRegion), nameof(ScopedOptionalRow.ReviewerCode));
        references.Single(reference => reference.NavigationName == nameof(ScopedOptionalRow.Origin)).OptionalPresenceColumns.Should().Equal(nameof(ScopedOptionalRow.OriginId));
        references.Single(reference => reference.NavigationName == nameof(ScopedOptionalRow.Actor)).OptionalPresenceColumns.Should().BeNull();
        var fixture = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        fixture.Should().Contain("matchesCreatedOwner:").And.Contain("ReviewerRegion is null").And.NotContain("Scope is null").And.NotContain("Zone is null").And.NotContain("Microsoft.EntityFrameworkCore");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("IndependentNullableComponentsAllNullAbsentAllPresentReferencePartialNullRejected")
            .And.Contain("AlwaysAgreeBeforeBindingIncludingAbsentReferences");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixturePermissionRemainsIndependentAndProductionContractsStayUnchanged(bool mapped)
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        Action missing = () => RepositoryGenerator.Generate(context, Builder(mapped, simulate: false).Build());
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void DisjointOptionalReferencesStayByteIdenticalEvenWithUnusedSharedPermission()
    {
        using var context = OptionalReferenceTestHarness.Context();
        var normal = RepositoryGenerator.Generate(context, OptionalReferenceTestHarness.Builder().Build());
        var shared = RepositoryGenerator.Generate(context, OptionalReferenceTestHarness.Builder(change: links => links.AllowSharedReferenceColumns()).Build());
        shared.Files.Select(file => (file.Kind, file.Name, file.Contents)).Should().Equal(normal.Files.Select(file => (file.Kind, file.Name, file.Contents)));
    }
}
