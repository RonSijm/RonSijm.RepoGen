using System.Linq.Expressions;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.OptionalJoinTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalJoinGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, false, false)]
    [InlineData(RepositoryOutputLayout.SingleFile, true, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true, true)]
    [InlineData(RepositoryOutputLayout.PerContract, false, false)]
    [InlineData(RepositoryOutputLayout.PerContract, true, true)]
    public void GuardedOperationsCompileAsExactEfFreeContracts(RepositoryOutputLayout layout, bool mapped, bool both)
    {
        using var context = Context(both);
        var design = Builder(mapped, both).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        var output = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        output.Should().Contain("requiredTarget: false").And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("ExplicitLeadingNonNullFilter").And.Contain("NotSimulated").And.Contain("joinRelationships");
    }

    [Theory]
    [InlineData("count")]
    [InlineData("projection")]
    [InlineData("any")]
    [InlineData("later")]
    [InlineData("or")]
    [InlineData("trailing")]
    [InlineData("include")]
    [InlineData("profile")]
    [InlineData("projectionProfile")]
    [InlineData("from")]
    public void AGuardMustProtectEveryUseBeforeObservationAndCannotAuthorizeLoading(string shape)
    {
        using var context = Context();
        var builder = Builder();
        var owner = builder.Entity<ManyReadOwner>();
        switch (shape)
        {
            case "count": owner.HasQuery("Unsafe").Select(row => row.Tags.Count).List(); break;
            case "projection": owner.HasQuery("Unsafe").Select(row => row.Tags.Select(tag => tag.Name).ToList()).List(); break;
            case "any": owner.HasQuery("Unsafe").Where(row => row.Tags.Any(tag => tag != null)).Select(row => row.Id).List(); break;
            case "later": owner.HasQuery("Unsafe").Select(row => row.Tags.OrderBy(tag => tag.Id).Where(tag => tag != null).Count()).List(); break;
            case "or": owner.HasQuery("Unsafe").Select(row => row.Tags.Where(tag => tag != null || tag!.Hidden).Count()).List(); break;
            case "trailing": owner.HasQuery("Unsafe").Select(row => row.Tags.Where(tag => !tag.Hidden && tag != null).Count()).List(); break;
            case "include": owner.HasQuery("Unsafe").Include(row => row.Tags).Select(row => row.Tags.Where(tag => tag != null).Count()).List(); break;
            case "profile":
                owner.HasLoadProfile("UnsafeLoad", load => load.Include(row => row.Tags));
                owner.HasQuery("Unsafe").UseLoadProfile("UnsafeLoad").Select(row => row.Tags.Where(tag => tag != null).Count()).List(); break;
            case "projectionProfile":
                owner.HasLoadProfile("UnsafeLoad", load => load.Include(row => row.Tags));
                owner.HasProjection("Guarded", row => row.Tags.Where(tag => tag != null).Count()).UseLoadProfile("UnsafeLoad");
                owner.HasQuery("Unsafe").ProjectTo("Guarded").List(); break;
            case "from": owner.HasQuery("Unsafe").From(sources => sources.Set<ManyReadOwner>().Select(row => row.Tags.Count)).List(); break;
        }
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*optional target*Where(target => target != null)*");
        builder.Configure(options => options.RequirePortableTestQueries(false));
        var design = builder.Build();
        var query = RepositoryModelFactory.Create(context, typeof(ManyReadContext).Assembly, design: design)
            .Entities.Single(entity => entity.EntityName == nameof(ManyReadOwner)).Queries.Single(query => query.MethodName == "UnsafeAsync");
        query.InMemoryQuery!.Supported.Should().BeFalse();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
    }

    [Fact]
    public void ReusedExpressionNodesCannotLeakAGuardToAnUnguardedCount()
    {
        using var context = Context();
        var row = Expression.Parameter(typeof(ManyReadOwner), "row");
        var tags = Expression.Property(row, nameof(ManyReadOwner.Tags));
        Expression<Func<ManyReadTag, bool>> guard = tag => tag != null;
        var where = Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [typeof(ManyReadTag)], tags, guard);
        var count = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(ManyReadTag)], where);
        var projection = Expression.Lambda<Func<ManyReadOwner, int>>(Expression.Add(count, Expression.Property(tags, "Count")), row);
        var builder = Builder();
        builder.Entity<ManyReadOwner>().HasQuery("Unsafe").Select(projection).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*optional target*");
    }

    [Fact]
    public void SavedResultsUseTheSameGuardRequirement()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<ManyReadOwner>().HasCommand("Unsafe").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "changed")).Save().ReturnsSaved(row => row.Tags.Count);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*optional target*");
    }

    [Fact]
    public void SourceNullabilityIsDirectionalAndNeedsNoArtificialGuardOnRequiredTargets()
    {
        using var context = Context();
        var design = Builder().Build();
        var model = RepositoryModelFactory.Create(context, typeof(ManyReadContext).Assembly, design: design);
        var tags = model.Entities.Single(entity => entity.EntityName == nameof(ManyReadTag));
        tags.Queries.Should().OnlyContain(query => query.InMemoryQuery!.Supported);
        var edge = tags.Queries.Single(query => query.MethodName == "LoadedOwnersAsync").InMemoryQuery!.Navigation!.Relationships.Single();
        edge.Join!.RequiredSource.Should().BeFalse();
        edge.Join.RequiredTarget.Should().BeTrue();
    }

    [Fact]
    public void TestOptOutDoesNotChangeTheProductionApiOrQueryFingerprint()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(queries: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
    }
}
