using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExplicitKeyBindingsCompileWithoutEntityGettersReflectionOrEfDependencies(RepositoryOutputLayout layout)
    {
        using var context = Context();
        MappedKeyOwner.GetterCalls.Value = 0;
        var design = Builder().Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        MappedKeyOwner.GetterCalls.Value.Should().Be(0);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("InMemoryRelationships.LoadCollection").And.Contain("InMemoryRelationships.LoadReference")
            .And.Contain("LoadFilteredManyToMany").And.Contain("entity.__repoGenProperty").And.NotContain("EntityFrameworkCore")
            .And.NotContain("System.Reflection").And.NotContain("get_Item");
        source.Split('\n').Where(line => line.Contains("InMemoryRelationships.Load", StringComparison.Ordinal))
            .Should().OnlyContain(line => !line.Contains("entity.Code", StringComparison.Ordinal));
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("mappedPropertyKeys").And.Contain("DetachedReadRelationshipMatchingAndUniquenessOnly")
            .And.Contain("FieldBacked").And.Contain("FieldOnly").And.Contain("Indexer").And.Contain("Shadow").And.Contain("JoinSource").And.Contain("JoinTarget");
    }

    [Fact]
    public void EachAccessedEdgeNeedsSeparatePermissionAndDefaultOutputDoesNotChange()
    {
        using var context = Context();
        var withoutPermission = Builder(permission: false, strict: false);
        var unsupported = RepositoryGenerator.Generate(context, withoutPermission.Build());
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts)
            .Should().BeEquivalentTo(unsupported.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts));
        enabled.CreateApiSnapshot().ToJson().Should().Be(unsupported.CreateApiSnapshot().ToJson());
        var disabled = RepositoryGenerator.Generate(context, Builder(queries: false).Build());
        disabled.Files.Should().BeEquivalentTo(RepositoryGenerator.Generate(context, Builder(permission: false, queries: false).Build()).Files);
        unsupported.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("mappedPropertyKeys");
        var builder = Base();
        builder.Entity<MappedKeyOwner>().HasInMemoryNavigation(row => row.Children); // Replaces, not merges, permission.
        builder.Entity<MappedKeyChild>().HasQuery("Read").Select(row => row.Owner == null ? "none" : row.Owner.Name).List();
        RepositoryGenerator.Generate(context, builder.Build()); // The inverse does not inherit or need the unused edge.
        builder.Entity<MappedKeyOwner>().HasQuery("Read").Select(row => row.Children.Count).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*relationship keys require a script*");
    }

    [Fact]
    public void DeclarationReplacementAndRemovalResetOnlyThatEdge()
    {
        var builder = Base();
        var owner = builder.Entity<MappedKeyOwner>();
        owner.HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: true, useMappedPropertyKeys: true);
        var first = builder.Build().Entities.Single(entity => entity.EntityType == typeof(MappedKeyOwner)).InMemoryNavigations;
        first.Single(edge => edge.Name == nameof(MappedKeyOwner.Tags)).UseMappedPropertyKeys.Should().BeTrue();
        first.Single(edge => edge.Name == nameof(MappedKeyOwner.Tags)).PreserveMissingTargets.Should().BeTrue();
        owner.HasInMemoryNavigation(row => row.Tags);
        var reset = builder.Build().Entities.Single(entity => entity.EntityType == typeof(MappedKeyOwner)).InMemoryNavigations;
        reset.Single(edge => edge.Name == nameof(MappedKeyOwner.Tags)).UseMappedPropertyKeys.Should().BeFalse();
        reset.Single(edge => edge.Name == nameof(MappedKeyOwner.Tags)).PreserveMissingTargets.Should().BeFalse();
        reset.Single(edge => edge.Name == nameof(MappedKeyOwner.Children)).UseMappedPropertyKeys.Should().BeTrue();
        owner.WithoutInMemoryNavigation(row => row.Tags).WithoutInMemoryNavigation(row => row.Tags);
        builder.Build().Entities.Single(entity => entity.EntityType == typeof(MappedKeyOwner)).InMemoryNavigations.Should().ContainSingle();
    }

    [Fact]
    public void IndexerForeignKeysDoNotProduceInvalidImplicitClrAccessorsButNamedBindingsStillWork()
    {
        using var context = Context();
        var builder = Base();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single(entity => entity.EntityName == nameof(MappedKeyLink)).Lookups.Should().NotContain(lookup => lookup.MethodName == "ByTagId");
        builder.Entity<MappedKeyLink>().HasAccessor<int?>("TagId");
        SavedResultTestHarness.CompileProduction(context, builder.Build());
        var explicitModel = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        explicitModel.Entities.Single(entity => entity.EntityName == nameof(MappedKeyLink)).Lookups.Should().Contain(lookup => lookup.MethodName == "ByTagId");
    }
}
