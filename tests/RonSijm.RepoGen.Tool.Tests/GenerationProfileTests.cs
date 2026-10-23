using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class GenerationProfileTests
{
    [Fact]
    public void SimpleDefaultsDisableOptionalHelpersButKeepDirectCrud()
    {
        using var context = Context();
        var design = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()).Build();
        var model = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: design);
        model.Settings.UseDbContextResolver.Should().BeFalse();
        model.Settings.UseAsyncContextLeases.Should().BeFalse();
        model.Settings.ContractNamespace.Should().BeNull();
        model.Settings.GenerateTrackingMethods.Should().BeFalse();
        var source = RepositorySourceRenderer.Render(model);
        source.Should().Contain("void Add(").And.Contain("Task AddAsync(").And.Contain("Task<int> SaveChangesAsync(");
        source.Should().NotContain("IRepositoryCacheProvider").And.NotContain("CacheDecorator").And.NotContain("AddRangeAsync(")
            .And.NotContain("maxConcurrencyRetries").And.NotContain("ExecuteInTransactionAsync").And.NotContain("ResolveContext()");
        RepositoryManifestRenderer.Render(model).Should().Contain("\"cacheDecorator\": false").And.Contain("\"batchOperations\": false");
    }

    [Fact]
    public void ExplicitOverridesAfterSimpleDefaultsOnlyEnableSelectedFeatures()
    {
        using var context = Context();
        var design = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateBatchOperations().GenerateCacheDecorator()).Build();
        var source = RepositorySourceRenderer.Render(RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: design));
        source.Should().Contain("AddRangeAsync(").And.Contain("CacheDecorator");
        source.Should().NotContain("ResolveContext()").And.NotContain("maxConcurrencyRetries").And.NotContain("ExecuteInTransactionAsync");
    }

    [Fact]
    public void PresetsDoNotEraseIndependentContractAndTrackingConfiguration()
    {
        var options = new RepositoryModelBuilder().Configure(builder => builder
            .UseClosedRepositories("App.Contracts").GenerateOnlyConfiguredEntities().UseDefaultTracking(RepositoryTrackingBehavior.NoTracking)
            .UseReadOnlyTracking(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution).UseTrackingMethodNames(noTracking: "AsReadOnly")
            .UseAsyncContextLeases().UseSimpleDefaults()).Build().Options;
        options.ContractNamespace.Should().Be("App.Contracts");
        options.GenerateOnlyConfiguredEntities.Should().BeTrue();
        options.DefaultTrackingBehavior.Should().Be(RepositoryTrackingBehavior.NoTracking);
        options.ReadOnlyTrackingBehavior.Should().Be(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution);
        options.NoTrackingMethodName.Should().Be("AsReadOnly");
        options.GenerateTrackingMethods.Should().BeFalse();
        options.UseAsyncContextLeases.Should().BeFalse();
    }

    [Fact]
    public void CompatibilityDefaultsMatchExistingDefaultsAndProfilesApplyBeforeDesignerOverrides()
    {
        using var context = Context();
        var compatibility = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseCompatibilityDefaults()).Build();
        var expected = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly);
        RepositorySourceRenderer.Render(RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: compatibility))
            .Should().Be(RepositorySourceRenderer.Render(expected));
        var overridden = new OverrideDesigner().CreateModel(options => options.UseSimpleDefaults());
        overridden.Options.GenerateBatchOperations.Should().BeTrue();
        overridden.Options.GenerateCacheDecorator.Should().BeFalse();
        overridden.Options.UseDbContextResolver.Should().BeFalse();
    }

    [Theory]
    [InlineData("simple", RepositoryGenerationProfile.Simple)]
    [InlineData("compatibility", RepositoryGenerationProfile.Compatibility)]
    [InlineData("SIMPLE", RepositoryGenerationProfile.Simple)]
    public void CliProfilesParseAndWorkWithoutAnyDesigner(string value, RepositoryGenerationProfile expected)
    {
        var project = Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
        GeneratorOptions.TryParse(["--project", project, "--profile", value], out var options, out var error).Should().BeTrue();
        error.Should().BeNull();
        options!.Profile.Should().Be(expected);
        var model = DbContextModelLoader.Load(typeof(ClosedContext).Assembly.Location, null, null, nameof(ClosedContext), false, options.Profile);
        model.Settings.GenerateCacheDecorator.Should().Be(expected == RepositoryGenerationProfile.Compatibility);
        model.Settings.UseDbContextResolver.Should().BeFalse();
    }

    [Theory]
    [InlineData("complex")]
    [InlineData("0")]
    public void UnknownProfilesAreRejected(string value)
    {
        var project = Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
        GeneratorOptions.TryParse(["--project", project, "--profile", value], out _, out var error).Should().BeFalse();
        error.Should().Contain("--profile must be");
        Action invalid = () => new RepositoryModelBuilder().Configure(options => options.UseProfile((RepositoryGenerationProfile)42));
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseInMemoryDatabase("profiles").Options);

    private sealed class OverrideDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder) =>
            modelBuilder.Configure(options => options.GenerateBatchOperations());
    }
}