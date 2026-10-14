using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OptionalReferenceTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void OptionalScalarAndCompositeReferencesCompileInEveryLayout(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build(); var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(OptionalReferenceContext).Assembly, design: design);
        var collections = model.Entities.Single(entity => entity.EntityName == nameof(OptionalReferenceOwner)).Commands.SelectMany(command => command.Collections ?? []).ToArray();
        foreach (var collection in collections)
        {
            collection.Link!.AdditionalReferences.Should().HaveCount(2).And.OnlyContain(reference => reference.Optional == true);
            collection.Link.AdditionalReferences![0].ForeignKeys.Should().Equal(nameof(OptionalReferenceRow.ReviewerRegion), nameof(OptionalReferenceRow.ReviewerCode));
        }
        var receipt = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        receipt.Should().Contain("AllNullAbsentAllPresentReferencePartialNullRejectedOnNewLinks").And.Contain("ApprovedInitializerOnceOnNewLinkBeforeThatRowIsStaged");
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("Microsoft.EntityFrameworkCore");
    }

    [Theory]
    [InlineData("requiredDeclaration")]
    [InlineData("unaccounted")]
    [InlineData("targetOptional")]
    [InlineData("incompleteMap")]
    [InlineData("updateReference")]
    [InlineData("graph")]
    [InlineData("missingTarget")]
    public void NullableReferencesAreExplicitNotGeneralWritePermissions(string invalid)
    {
        using var context = Context(); var builder = Builder(change: links =>
        {
            switch (invalid)
            {
                case "requiredDeclaration": links.WithoutAdditionalReference(row => row.Reviewer).AdditionalReference(row => row.Reviewer); break;
                case "unaccounted": links.WithoutAdditionalReference(row => row.Source); break;
                case "targetOptional": links.TargetReference(row => row.Source); break;
                case "incompleteMap": links.Add(item => new OptionalReferenceRow { TargetId = item.TargetId, ReviewerRegion = item.ReviewerRegion, SourceId = item.SourceId }); break;
                case "updateReference": links.AllowWrite(row => row.SourceId).Update(setters => setters.SetProperty(row => row.SourceId, (row, item) => item.SourceId)); break;
                case "graph": links.Add(item => new OptionalReferenceRow { TargetId = item.TargetId, ReviewerRegion = item.ReviewerRegion, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId, Source = new OptionalReferenceSource() }); break;
                case "missingTarget": links.WithoutTargetReference(); break;
            }
        });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid switch { "incompleteMap" => "RGD4202:*", "updateReference" or "graph" => "RGD2802:*", _ => "RGD4201:*" });
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("generated")]
    [InlineData("required")]
    [InlineData("overlap")]
    public void UnrepresentableAbsentKeysAndWrongCardinalityRequireAnAdapter(string invalid)
    {
        using OptionalReferenceContext context = invalid switch { "mixed" => new OptionalReferenceMixedContext(Options()), "generated" => new OptionalReferenceGeneratedContext(Options()), "overlap" => new OptionalReferenceOverlapContext(Options()), _ => new OptionalReferenceRequiredContext(Options()) };
        Action generate = () => RepositoryGenerator.Generate(context, Builder().Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid is "required" or "overlap" ? "RGD4201:*" : "RGD4301:*");
    }

    [Fact]
    public void ACommandCanMixExplicitRequiredAndOptionalRolesWithoutChangingTheRequiredReceipt()
    {
        using var context = new OptionalReferenceRequiredContext(Options());
        var design = Builder(change: links => links.WithoutAdditionalReference(row => row.Source).AdditionalReference(row => row.Source)).Build();
        var bundle = RepositoryGenerator.Generate(context, design);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var model = RepositoryModelFactory.Create(context, typeof(OptionalReferenceContext).Assembly, design: design);
        var references = model.Entities.Single(entity => entity.EntityName == nameof(OptionalReferenceOwner)).Commands.First(command => command.Collections is not null).Collections![0].Link!.AdditionalReferences!;
        references.Single(reference => reference.NavigationName == nameof(OptionalReferenceRow.Reviewer)).Optional.Should().BeTrue();
        references.Single(reference => reference.NavigationName == nameof(OptionalReferenceRow.Source)).Optional.Should().BeNull();
    }

    [Fact]
    public void OptionalDeclarationDoesNotEnableFixturesOrBypassProtection()
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(simulate: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        Action strict = () => RepositoryGenerator.Generate(context, Builder(simulate: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        var protectedBuilder = Builder(); protectedBuilder.Entity<OptionalReferenceRow>().ProtectWrite(row => row.ReviewerRegion);
        Action protect = () => RepositoryGenerator.Generate(context, protectedBuilder.Build());
        protect.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
        var permitted = Builder(change: links => links.AllowWrite(row => row.ReviewerRegion));
        permitted.Entity<OptionalReferenceRow>().ProtectWrite(row => row.ReviewerRegion);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, permitted.Build()));
    }

    [Fact]
    public void RequiredAndOptionalDeclarationsShareDuplicateAndRemovalRules()
    {
        Action duplicate = () => Builder(change: links => links.AdditionalReference(row => row.Reviewer));
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD4201:*already declared*");
        Action nested = () => Builder(change: links => links.OptionalAdditionalReference(row => row.Owner!.Links));
        nested.Should().Throw<ArgumentException>().WithMessage("RGD4201:*direct*");
        using var context = Context(); var removed = Builder(change: links => links.WithoutAdditionalReference(row => row.Reviewer).OptionalAdditionalReference(row => row.Reviewer));
        RepositoryGenerator.Generate(context, removed.Build()).Files.Select(file => (file.Kind, file.Name, file.Contents))
            .Should().Equal(RepositoryGenerator.Generate(context, Builder().Build()).Files.Select(file => (file.Kind, file.Name, file.Contents)));
    }

    [Fact]
    public void RemovingAnOptionalDeclarationPreservesUnconfiguredOutput()
    {
        RepositoryDesign Design(bool removed)
        {
            var builder = LinkWriteTestHarness.Builder();
            builder.Entity<LinkWriteOwner>().HasCommand("Probe").Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
                .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code), links =>
                    {
                        if (removed) links.OptionalAdditionalReference(row => row.Target).WithoutAdditionalReference(row => row.Target);
                    }).Save().SimulateCollectionWrites();
            return builder.Build();
        }
        using var context = LinkWriteTestHarness.Context();
        RepositoryGenerator.Generate(context, Design(true)).Files.Select(file => (file.Kind, file.Name, file.Contents))
            .Should().Equal(RepositoryGenerator.Generate(context, Design(false)).Files.Select(file => (file.Kind, file.Name, file.Contents)));
    }
}
