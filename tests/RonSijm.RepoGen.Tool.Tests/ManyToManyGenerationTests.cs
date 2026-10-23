using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyToManyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void BothStoreShapesCompileInEveryLayoutWithNoOrmInTestOutput(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents))
            .Should().Contain("InMemoryRelationships.LoadManyToMany").And.Contain("entity.TenantId, entity.OwnerCode")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
    }

    [Fact]
    public void ReceiptsDescribeJoinStoresBothForeignKeysAndOnlyAccessedEdges()
    {
        using var context = Context(); var design = Builder().Build();
        var model = RepositoryModelFactory.Create(context, typeof(ManyReadContext).Assembly, design: design);
        var queries = model.Entities.Single(entity => entity.EntityName == nameof(ManyReadOwner)).Queries;
        var cards = queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!;
        cards.Supported.Should().BeTrue(); cards.Navigation!.Roots.Should().HaveCount(3); cards.Navigation.Relationships.Should().ContainSingle();
        var join = cards.Navigation.Relationships.Single().Join!;
        join.EntityTypeName.Should().Be(RepositoryModelFactory.CSharpTypeName(typeof(ManyReadLink)));
        join.RequiredSource.Should().BeTrue(); join.RequiredTarget.Should().BeTrue();
        queries.Single(query => query.MethodName == "CyclesAsync").InMemoryQuery!.Navigation!.Relationships.Should().HaveCount(2);
        var manifest = RepositoryGenerator.Generate(context, design).Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("joinRelationships").And.Contain("UniqueOrdinaryStructuralKeys").And.Contain("JoinSnapshotOrder")
            .And.Contain("NoImplicitRepoGenPolicyInjection");
    }

    [Theory]
    [InlineData("store")]
    [InlineData("excluded")]
    [InlineData("map")]
    [InlineData("key")]
    [InlineData("mixed")]
    [InlineData("declaration")]
    public void EveryParticipatingTableAndKeyNeedsAnExplicitSelectedAdapter(string scenario)
    {
        using var context = Context(); var builder = Builder(strict: false);
        var link = builder.Entity<ManyReadLink>();
        switch (scenario)
        {
            case "store": link.WithoutInMemoryStore(); break;
            case "excluded": link.GenerateTestDouble(false); break;
            case "map": link.HasInMemoryStore<ManyReadLinkData, int>(row => row.Id, row => new(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden)); break;
            case "key": link.MapInMemoryEntity<ManyReadLinkData>(row => new ManyReadLink { Id = row.Id }); break;
            case "mixed": builder.Entity<ManyReadOwner>().HasInMemoryStore(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new ManyReadOwner { TenantId = row.TenantId, Id = row.Id, Code = row.Code, Name = row.Name, Hidden = row.Hidden }); break;
            case "declaration": builder.Entity<ManyReadOwner>().WithoutInMemoryNavigation(row => row.Tags); break;
        }
        var design = builder.Build(); var model = RepositoryModelFactory.Create(context, typeof(ManyReadContext).Assembly, design: design);
        var coverage = model.Entities.Single(entity => entity.EntityName == nameof(ManyReadOwner)).Queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().NotBeEmpty();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("comparer")]
    [InlineData("autoInclude")]
    public void JoinAndEndpointProviderPoliciesAreNotSilentlyDiscarded(string scenario)
    {
        var options = new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options;
        using ManyReadContext context = scenario switch { "filter" => new ManyReadFilteredContext(options), "comparer" => new ManyReadComparedContext(options), _ => new ManyReadAutoIncludeContext(options) };
        var builder = Builder(strict: false);
        builder.Entity<ManyReadOwner>().HasQuery("Bypassed").IgnoreQueryFilters().Select(row => row.Tags.Count).List();
        var queries = RepositoryModelFactory.Create(context, typeof(ManyReadContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(ManyReadOwner)).Queries;
        queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!.Supported.Should().BeFalse();
        queries.Single(query => query.MethodName == "BypassedAsync").InMemoryQuery!.Supported.Should().Be(scenario == "filter");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void TestOnlyOptOutsDoNotChangeProductionContractsOrApi()
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        foreach (var builder in new[] { Builder(navigation: false, strict: false), Builder(queries: false) })
        {
            var disabled = RepositoryGenerator.Generate(context, builder.Build());
            foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
                disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
            disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
            string.Join("\n", disabled.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("InMemoryRelationships.LoadManyToMany");
        }
    }
}
