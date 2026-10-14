using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NullTargetReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NullTargetReadGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true, true)]
    public void ExplicitNullSafeQueriesAndSavedResultsCompileInEveryLayout(RepositoryOutputLayout layout, bool mapped, bool both)
    {
        using var context = OptionalJoinTestHarness.Context(both); var design = Builder(mapped, both).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Should().Contain("preserveMissingTargets: true").And.NotContain("Microsoft.EntityFrameworkCore");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should()
            .Contain("PreserveMissingTargetPerJoinRow").And.Contain("ExplicitNullSafeClrExpressionsOnly");
    }

    [Theory]
    [InlineData("select")]
    [InlineData("sort")]
    [InlineData("include")]
    [InlineData("profile")]
    [InlineData("command")]
    public void TheOptionCannotAuthorizeUnsafeExpressionsOrExplicitLoading(string invalid)
    {
        using var context = OptionalJoinTestHarness.Context(); var builder = Builder(); var owner = builder.Entity<ManyReadOwner>();
        switch (invalid)
        {
            case "select": owner.HasQuery("Unsafe").Select(row => row.Tags.Select(tag => tag.Name).ToList()).List(); break;
            case "sort": owner.HasQuery("Unsafe").Select(row => row.Tags.OrderBy(tag => tag.Id).Count()).List(); break;
            case "include": owner.HasQuery("Unsafe").Include(row => row.Tags).Select(row => row.Tags.Count).List(); break;
            case "profile": owner.HasLoadProfile("Raw", load => load.Include(row => row.Tags)); owner.HasQuery("Unsafe").UseLoadProfile("Raw").Select(row => row.Tags.Count).List(); break;
            case "command": owner.HasCommand("Unsafe").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "changed"))
                    .Save().ReturnsSaved(row => row.Tags.Select(tag => tag.Name).ToList()); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(invalid == "command" ? "RGD3402:*" : "RGD3302:*");
    }

    [Fact]
    public void NullPreservationIsOffByDefaultAndDoesNotChangeProductionOrTheContract()
    {
        using var context = OptionalJoinTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(preserve: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        Action strict = () => RepositoryGenerator.Generate(context, Builder(preserve: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Where(target => target != null)*");
    }

    [Fact]
    public void ResettingTheOptionPreservesTheExistingGuardedOutputAndDisablingFixtureReadsIgnoresIt()
    {
        using var context = OptionalJoinTestHarness.Context(); var normal = OptionalJoinTestHarness.Builder(); var reset = OptionalJoinTestHarness.Builder();
        reset.Entity<ManyReadOwner>().HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: true).HasInMemoryNavigation(row => row.Tags);
        RepositoryGenerator.Generate(context, reset.Build()).Files.Select(file => (file.Kind, file.Name, file.Contents))
            .Should().Equal(RepositoryGenerator.Generate(context, normal.Build()).Files.Select(file => (file.Kind, file.Name, file.Contents)));
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(queries: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
    }

    [Theory]
    [InlineData("required")]
    [InlineData("inverse")]
    [InlineData("ordinary")]
    public void PreservationRequiresAnOptionalTargetSkipNavigation(string invalid)
    {
        Action generate;
        if (invalid == "ordinary")
        {
            using var context = OptionalReferenceTestHarness.Context(); var builder = OptionalReferenceTestHarness.Builder();
            builder.Entity<OptionalReferenceRow>().HasInMemoryNavigation(row => row.Source, preserveMissingTargets: true);
            generate = () => RepositoryGenerator.Generate(context, builder.Build());
            generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4401:*"); return;
        }
        using var many = invalid == "required" ? ManyReadTestHarness.Context() : OptionalJoinTestHarness.Context();
        var design = invalid == "required" ? ManyReadTestHarness.Builder() : OptionalJoinTestHarness.Builder();
        if (invalid == "required") design.Entity<ManyReadOwner>().HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: true);
        else design.Entity<ManyReadTag>().HasInMemoryNavigation(row => row.Owners, preserveMissingTargets: true);
        generate = () => RepositoryGenerator.Generate(many, design.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4401:*");
    }
}
