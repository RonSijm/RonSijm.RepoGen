using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.RelationshipKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipKeyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public async Task OrdinaryAndManyToManyRelationshipsCompileInAllLayoutsWithoutOrmLeaks(RepositoryOutputLayout layout, bool mapped)
    {
        using var navigation = NavigationContext(); using var many = ManyContext();
        foreach (var pair in new[] { (Context: (DbContext)navigation, Builder: NavigationBuilder(mapped)), (Context: many, Builder: ManyBuilder(mapped)) })
        {
            if (pair.Context is ManyReadContext)
                pair.Builder.Entity<ManyReadTag>().HasInMemoryRelationshipComparer(row => row.Id, () => EqualityComparer<int>.Default);
            var design = pair.Builder.Build(); var bundle = RepositoryGenerator.Generate(pair.Context, design, layout);
            var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(pair.Context, design);
            var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
            source.Should().Contain("InMemoryRelationshipKeyComparer").And.Contain("StringComparer.OrdinalIgnoreCase")
                .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Reflection");
            bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should()
                .Contain("FixtureReadRelationshipsAndUniquenessOnly").And.Contain("NeverJoin").And.Contain("NotInferred");
            if (pair.Context is ManyReadContext)
            {
                var (owner, _, _) = ManyRepositories(mapped, assembly: assembly);
                (await ManyReadTestHarness.Invoke<Task<IReadOnlyList<ManyReadCard>>>(owner, "CardsAsync"))[0].Tags.Should().Equal("Red", "Blue");
                source.Should().Contain("targetKeyComparer:").And.Contain("Component<" + RepositoryModelFactory.CSharpTypeName(typeof(int)) + ">");
            }
        }
    }

    [Fact]
    public void ResetOptOutAndDisabledQueriesRetainExactExistingOutputAndProductionApi()
    {
        using var context = ManyContext(); var builder = ManyBuilder(comparer: false);
        var baseline = RepositoryGenerator.Generate(context, builder.Build());
        var owner = builder.Entity<ManyReadOwner>(); owner.HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        var configured = RepositoryGenerator.Generate(context, builder.Build());
        foreach (var file in baseline.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            configured.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        configured.CreateApiSnapshot().ToJson().Should().Be(baseline.CreateApiSnapshot().ToJson());
        owner.WithoutInMemoryRelationshipComparer(row => row.Code);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(baseline.Files);
        var disabled = NavigationBuilder(queries: false);
        using var first = NavigationContext();
        var off = RepositoryGenerator.Generate(first, disabled.Build());
        disabled.Entity<NavigationReadLibrary>().WithoutInMemoryRelationshipComparer(row => row.Code);
        using var second = NavigationContext();
        RepositoryGenerator.Generate(second, disabled.Build()).Files.Should().BeEquivalentTo(off.Files);
    }

    [Fact]
    public void StoreReplacementRemovesOnlyItsRelationshipPoliciesAndDuplicateDeclarationsAreRejected()
    {
        var builder = ManyBuilder(); var entity = builder.Entity<ManyReadOwner>();
        Action duplicate = () => entity.HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.Ordinal);
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD4501:*");
        entity.WithoutInMemoryRelationshipComparer(row => row.Code).HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.Ordinal);
        entity.HasInMemoryStore(row => row.Id, row => new ManyReadOwner { Id = row.Id });
        builder.Build().Entities.Single(row => row.EntityType == typeof(ManyReadOwner)).InMemoryStore!.RelationshipComparers.Should().BeEmpty();
        entity.WithoutInMemoryStore();
        Action missing = () => entity.HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.Ordinal);
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGD4501:*");
        Action nullFactory = () => entity.HasInMemoryRelationshipComparer<string>(row => row.Code, null!);
        nullFactory.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("foreign")]
    [InlineData("mismatch")]
    public void OnlyExactPrincipalKeyComponentsCanDeclareThePolicy(string scenario)
    {
        using var context = NavigationContext(); var builder = NavigationBuilder(comparer: false);
        if (scenario == "payload") builder.Entity<NavigationReadLibrary>().HasInMemoryRelationshipComparer(row => row.Name, () => StringComparer.Ordinal);
        if (scenario == "foreign") builder.Entity<NavigationReadBook>().HasInMemoryRelationshipComparer(row => row.LibraryCode, () => StringComparer.Ordinal);
        if (scenario == "mismatch") builder.Entity<NavigationReadLibrary>().HasInMemoryRelationshipComparer<object>(row => row.Code, () => EqualityComparer<object>.Default);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4501:*");
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("culture")]
    [InlineData("getter")]
    [InlineData("call")]
    [InlineData("null")]
    [InlineData("cast")]
    [InlineData("entity")]
    [InlineData("infrastructure")]
    public void FactoriesAreValidatedWithoutExecutingCapturedOrAmbientBehavior(string scenario)
    {
        using var context = ManyContext(); var builder = ManyBuilder(comparer: false);
        IEqualityComparer<string> captured = StringComparer.OrdinalIgnoreCase;
        Expression<Func<IEqualityComparer<string>>> factory = scenario switch
        {
            "capture" => () => captured,
            "culture" => () => StringComparer.CurrentCulture,
            "getter" => () => ThrowingFactory.Comparer,
            "call" => () => StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, true),
            "cast" => () => (IEqualityComparer<string>)(object)new object(),
            "entity" => () => new RelationshipKeyEntityComparer(new ManyReadOwner()),
            "infrastructure" => () => new MemoryKeyInfrastructureComparer(new List<DbContext>()),
            _ => () => null!
        };
        builder.Entity<ManyReadOwner>().HasInMemoryRelationshipComparer(row => row.Code, factory);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4501:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitClrComparersStillRequireADesignDeclaration(bool many)
    {
        using DbContext context = many ? new RelationshipManyComparedContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options) : NavigationContext();
        var builder = many ? ManyBuilder(comparer: false) : NavigationBuilder(comparer: false);
        builder.Configure(options => options.RequirePortableTestCommands(false).RequirePortableTestQueries(false));
        var without = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        without.Entities.SelectMany(entity => entity.Queries).Should().Contain(query => query.InMemoryQuery!.Reasons.Any(reason => reason.Contains("custom key", StringComparison.Ordinal)));
        if (many) builder.Entity<ManyReadOwner>().HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        else builder.Entity<NavigationReadLibrary>().HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        builder.Configure(options => options.RequirePortableTestQueries());
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.SelectMany(entity => entity.Queries).Should().OnlyContain(query => query.InMemoryQuery!.Supported);
        if (many) model.Entities.SelectMany(entity => entity.Commands).Should().Contain(command => !command.InMemoryCommand!.Supported, "read relationship comparison cannot authorize tracked key writes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertersAndProviderComparersCannotBeAcknowledgedByRelationshipFactories(bool provider)
    {
        var options = new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options;
        using RelationshipNavigationContext context = provider ? new RelationshipNavigationProviderContext(options) : new RelationshipNavigationConvertedContext(options);
        var builder = NavigationBuilder();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*custom key conversion/comparison*");
    }

    private static class ThrowingFactory
    {
        public static IEqualityComparer<string> Comparer => throw new InvalidOperationException("Must never execute during generation.");
    }

    [Fact]
    public void DisabledReadsDoNotEvaluateOrValidateAnUnusedFactory()
    {
        using var context = NavigationContext(); var builder = NavigationBuilder(comparer: false, queries: false);
        builder.Entity<NavigationReadLibrary>().HasInMemoryRelationshipComparer(row => row.Code, () => ThrowingFactory.Comparer);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        ScriptedTestDoubleTestHarness.Compile(bundle);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("InMemoryRelationshipKeyComparer");
    }

    [Fact]
    public void AComparisonPolicyCannotDeclareAnOtherwiseMissingNavigation()
    {
        using var context = NavigationContext(); var builder = NavigationBuilder();
        builder.Entity<NavigationReadLibrary>().WithoutInMemoryNavigation(row => row.Books);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*HasInMemoryNavigation*");
    }
}
