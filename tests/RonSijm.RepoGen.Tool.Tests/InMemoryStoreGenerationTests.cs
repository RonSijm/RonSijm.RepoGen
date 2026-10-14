using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryStoreGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void FixtureAndStoresCompileOnceInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(), layout);
        var source = Source(bundle);
        source.Split("public sealed class MemoryFixtureContextRepositoryFixture").Should().HaveCount(2);
        source.Split("public sealed class MemoryFixtureContextRepositoryData").Should().HaveCount(2);
        source.Should().Contain("MemoryFixtureRowStore").And.Contain("MemoryFixtureLabelStore").And.NotContain("MemoryFixtureScriptOnlyStore");
        source.Should().NotContain("EntityFramework").And.NotContain("IQueryable");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void UndeclaredOrDisabledStoreConfigurationLeavesScriptedOutputsExactlyUnchanged()
    {
        using var context = Context();
        var withDefinitions = RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(stores: false));
        var withoutDefinitions = RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(stores: false, declarations: false));
        withDefinitions.Files.Should().Equal(withoutDefinitions.Files);
        Source(withDefinitions).Should().NotContain("InMemoryStore").And.NotContain(" Fixture { get; }");
        var enabled = RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner());
        enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts)
            .Should().Equal(withDefinitions.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts));
        enabled.CreateApiSnapshot().ToJson().Should().Be(withDefinitions.CreateApiSnapshot().ToJson());
        var expected = System.Text.Json.Nodes.JsonNode.Parse(withDefinitions.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents)!;
        var actual = System.Text.Json.Nodes.JsonNode.Parse(enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents)!;
        actual.AsObject().Remove("testDoubles");
        expected.AsObject().Remove("testDoubles");
        actual.ToJsonString().Should().Be(expected.ToJsonString());
    }

    [Theory]
    [InlineData(RepositoryGenerationProfile.Simple)]
    [InlineData(RepositoryGenerationProfile.Compatibility)]
    public void ProfilesKeepStoresOffUnlessExplicitlySelected(RepositoryGenerationProfile profile)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseProfile(profile));
        builder.Build().Options.InMemoryTestStores.Should().BeFalse();
        builder.Configure(options => options.UseInMemoryTestStores().UseProfile(profile));
        builder.Build().Options.InMemoryTestStores.Should().BeTrue("presets must not erase an explicit test setting");
        builder.Configure(options => options.UseInMemoryTestStores(false));
        builder.Build().Options.InMemoryTestStores.Should().BeFalse();
    }

    [Fact]
    public void StoreRemovalAndEntityTestExclusionDoNotRemoveOtherFixturesOrProductionOperations()
    {
        using var context = Context();
        var builder = Builder();
        ConfigureStores(builder);
        builder.Entity<MemoryFixtureRow>().WithoutInMemoryStore();
        var first = RepositoryGenerator.Generate(context, builder.Build());
        Source(first).Should().NotContain("MemoryFixtureRowStore").And.Contain("MemoryFixtureRowRepositoryTestDouble").And.Contain("MemoryFixtureLabelStore");
        builder.Entity<MemoryFixtureRow>().GenerateTestDouble(false);
        var second = RepositoryGenerator.Generate(context, builder.Build());
        Source(second).Should().NotContain("MemoryFixtureRowRepositoryTestDouble").And.Contain("MemoryFixtureLabelStore");
        first.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories)
            .Should().Equal(second.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories));
        ScriptedTestDoubleTestHarness.Compile(second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestingNamespaceAndClosedContractsAreRequired(bool closed)
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseInMemoryTestStores());
        if (closed) builder.Configure(options => options.UseClosedRepositories("Generated.Contracts"));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3201:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtLeastOneSelectedStoreIsRequired(bool excluded)
    {
        using var context = Context();
        var builder = Builder();
        if (excluded) builder.Entity<MemoryFixtureRow>().HasInMemoryStore(row => row.Id, row => new MemoryFixtureRow { Id = row.Id }).GenerateTestDouble(false);
        builder.Entity<MemoryFixtureLabel>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3201:*HasInMemoryStore*");
    }

    [Fact]
    public void CapturedConfigurationStateAndInaccessibleKeysFailBeforeEmittingFiles()
    {
        using var context = Context();
        var builder = Builder();
        var offset = 1;
        var entity = builder.Entity<MemoryFixtureRow>();
        entity.HasInMemoryStore(row => row.Id + offset, row => new MemoryFixtureRow { Id = row.Id });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3201:*captures configuration-time state*");
        entity.HasInMemoryStore(row => new HiddenMemoryKey(row.Id), row => new MemoryFixtureRow { Id = row.Id });
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3201:*public entity/key types*");
    }

    [Fact]
    public void InternalDatabaseEntitiesRequireAnExplicitPublicStorageShape()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<HiddenMemoryEntity>();
        entity.WriteEnabled = false;
        entity.HasQuery("Count").Count();
        entity.HasInMemoryStore(row => row.Id, row => new HiddenMemoryEntity { Id = row.Id });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3201:*public data shape*");
        entity.HasInMemoryStore<MemoryRowView, int>(row => row.Id, row => new MemoryRowView(row.Id, row.Name));
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        Source(bundle).Should().Contain("InMemoryStore<global::RonSijm.RepoGen.Tool.Tests.MemoryRowView, global::System.Int32>")
            .And.NotContain("CreateStore<global::RonSijm.RepoGen.Tool.Tests.HiddenMemoryEntity");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        using var json = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        json.RootElement.GetProperty("testDoubles").GetProperty("stores").GetProperty("entities")[0].GetProperty("shape")
            .GetString().Should().Be("ExplicitDataShape");
    }

    [Fact]
    public void NullAdaptersAreRejectedAndFixtureControlNameCannotCollideWithDefaultPortHelpers()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<MemoryFixtureRow>();
        Action nullKey = () => entity.HasInMemoryStore<int>(null!, row => new MemoryFixtureRow { Id = row.Id });
        nullKey.Should().Throw<ArgumentNullException>();
        Action nullCopy = () => entity.HasInMemoryStore<int>(row => row.Id, null!);
        nullCopy.Should().Throw<ArgumentNullException>();
        entity.HasInMemoryStore(row => row.Id, row => new MemoryFixtureRow { Id = row.Id });
        entity.BindReadPort<IConflictingMemoryFixturePort>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3202:*");
    }

    [Fact]
    public void ReservedFixturePropertyAlsoRejectsGenericDefaultHelpersAbsentFromOperationInventory()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MemoryFixtureRow>().HasInMemoryStore(row => row.Id, row => new MemoryFixtureRow { Id = row.Id });
        builder.Entity<MemoryFixtureLabel>().BindReadPort<IGenericConflictingMemoryFixturePort>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3202:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManifestRecordsStorageAdaptersAndNeverClaimsDesignerExecution(bool keyed)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(keyed: keyed));
        using var json = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var testing = json.RootElement.GetProperty("testDoubles");
        testing.GetProperty("mode").GetString().Should().Be("Scripted");
        var stores = testing.GetProperty("stores");
        stores.GetProperty("copyPolicy").GetString().Should().Be("ExplicitDetachedAdapter");
        stores.GetProperty("executesDesignerPipeline").GetBoolean().Should().BeFalse();
        stores.GetProperty("contextKey").ValueKind.Should().Be(keyed ? JsonValueKind.String : JsonValueKind.Null);
        stores.GetProperty("entities").EnumerateArray().Should().HaveCount(2);
        testing.GetProperty("repositories").EnumerateArray().SelectMany(item => item.GetProperty("operations").EnumerateArray())
            .Should().OnlyContain(operation => operation.GetProperty("status").GetString() == "RequiresOverride");
        RepositoryGeneratedTypes.Names(bundle.Model).Should().Contain("Generated.Memory.TestDoubles.MemoryFixtureContextRepositoryFixture")
            .And.Contain("Generated.Memory.TestDoubles.MemoryFixtureContextRepositoryData");
    }

    internal static MemoryFixtureContext Context() => new(new DbContextOptionsBuilder<MemoryFixtureContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Memory.Contracts").UseGeneratedNamespace("Generated.Memory.Persistence")
        .UseScriptedTestDoubles("Generated.Memory.TestDoubles").UseInMemoryTestStores().GenerateOnlyConfiguredEntities());
    internal static string Source(RepositoryGenerationBundle bundle) => string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
    private static void ConfigureStores(RepositoryModelBuilder builder)
    {
        builder.Entity<MemoryFixtureRow>().HasInMemoryStore(row => row.Id, row => new MemoryFixtureRow { Id = row.Id });
        builder.Entity<MemoryFixtureLabel>().HasInMemoryStore(row => row.Code, row => new MemoryFixtureLabel { Code = row.Code });
    }
}
