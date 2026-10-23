using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ShadowReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExplicitShadowReadRowsCompileInEveryLayoutWithoutEfOrPersistenceDependencies(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var design = Builder().Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("__repoGenProperty").And.NotContain("EntityFrameworkCore").And.NotContain("EF.Property")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.ShadowReadNote").And.NotContain("InMemoryPropertyRead");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("MapInMemoryProperty")
            .And.Contain("DetachedReadRowsOnly").And.Contain("NotSimulated").And.Contain("Fingerprint");
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: design);
        var notes = model.Entities.Single(entity => entity.EntityName == nameof(ShadowReadNote));
        notes.Queries.Single(query => query.MethodName == "AllAsync").InMemoryQuery!.PropertyReads!.Select(read => read.PropertyName)
            .Should().NotContain("Deleted"); // An ignored model filter must not leave a read dependency.
        notes.Queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!.PropertyReads!.Select(read => read.PropertyName)
            .Should().Contain("Deleted");
    }

    [Fact]
    public void ReadBindingsNeverChangeProductionContractsOrApiAndDisabledBackendIsByteIdentical()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(bindings: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories))
        {
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        }
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(queries: false).Build()).Files.Should()
            .BeEquivalentTo(RepositoryGenerator.Generate(context, Builder(bindings: false, queries: false).Build()).Files);
    }

    [Fact]
    public void ReplacingRemovingAndClearingMappingsDoNotLeaveStaleBindings()
    {
        var builder = Base();
        var entity = builder.Entity<ShadowReadNote>();
        entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => row.Rank + 1);
        var store = builder.Build().Entities.Single(item => item.EntityType == typeof(ShadowReadNote)).InMemoryStore!;
        store.PropertyMappings.Count(property => property.PropertyName == "Rank").Should().Be(1);
        store.PropertyMappings.Single(property => property.PropertyName == "Rank").Value.ToString().Should().Contain("+ 1");
        entity.WithoutInMemoryProperty("Rank");
        store.PropertyMappings.Should().NotContain(property => property.PropertyName == "Rank");
        entity.WithoutInMemoryEntityMapping();
        store.PropertyMappings.Should().BeEmpty();
        entity.MapInMemoryEntity<ShadowReadSeed>(row => new ShadowReadNote { Id = row.Id })
            .MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => row.Rank);
        entity.HasInMemoryStore<ShadowReadSeed, int>(row => row.Id, row => row);
        builder.Build().Entities.Single(item => item.EntityType == typeof(ShadowReadNote)).InMemoryStore!.PropertyMappings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rank")]
    [InlineData("Folder")]
    public void NamesMustMatchExactFinalModelScalarProperties(string name)
    {
        using var context = Context();
        var builder = Base();
        builder.Entity<ShadowReadNote>().MapInMemoryProperty<ShadowReadSeed, int>(name, row => row.Rank);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*mapped scalar*exact*type*");
    }

    [Fact]
    public void BindingValueTypesCannotBeWidenedOrBoxed()
    {
        using var context = Context();
        var builder = Base();
        builder.Entity<ShadowReadNote>().MapInMemoryProperty<ShadowReadSeed, long>("Rank", row => row.Rank);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*exact*type*");
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("helper")]
    [InlineData("clock")]
    public void ValuesMustBeSelfContainedPortableScalarRecipesAndAreNeverExecuted(string mode)
    {
        using var context = Context();
        var builder = Base();
        var entity = builder.Entity<ShadowReadNote>();
        var captured = new ShadowReadRejectedValues();
        if (mode == "capture")
        {
            entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => captured.Value);
        }
        else if (mode == "helper")
        {
            entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => ShadowReadRejectedValues.Reject(row.Rank));
        }
        else
        {
            entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => DateTime.Now.Year);
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*");
    }

    [Fact]
    public void MapRequiresExplicitMatchingDataStoreAndReadMap()
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<ShadowReadNote>();
        Action noStore = () => entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", row => row.Rank);
        noStore.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*");
        entity.HasInMemoryStore(row => row.Id, row => row);
        noStore.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*");
        entity.HasInMemoryStore<ShadowReadSeed, int>(row => row.Id, row => row);
        noStore.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*");
        entity.MapInMemoryEntity<ShadowReadSeed>(row => new ShadowReadNote { Id = row.Id });
        noStore.Should().NotThrow();
        Action empty = () => entity.MapInMemoryProperty<ShadowReadSeed, int>(" ", row => row.Rank);
        empty.Should().Throw<ArgumentException>();
        Action nullValue = () => entity.MapInMemoryProperty<ShadowReadSeed, int>("Rank", null!);
        nullValue.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SyntheticMembersAvoidClrMemberCollisionsAndPropertyNamesNeverBecomeCode()
    {
        using var context = Context();
        var builder = Base();
        builder.Entity<ShadowReadNote>().MapInMemoryProperty<ShadowReadSeed, int>("tag.with\"quotes", row => row.Rank)
            .HasQuery("Read").Select(row => EF.Property<int>(row, "tag.with\"quotes")).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("EF.Property")
            .And.NotContain("tag.with").And.Contain("__repoGenProperty0_");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }
}
