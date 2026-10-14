using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryQueryGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryPortableShapeCompilesInSeparateEfFreeTestingOutput(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new PortableReadRepositoryDesigner(strict: true), layout);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Should().OnlyContain(file =>
            !file.Contents.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) && !file.Contents.Contains("System.Linq.Queryable.", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledQueriesPreserveProductionContractsApiAndCacheFingerprints()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, new PortableReadRepositoryDesigner());
        var disabled = RepositoryGenerator.Generate(context, new PortableReadRepositoryDesigner(enabled: false));
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles)
            .Select(file => file.Contents).Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        var enabledModel = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: new PortableReadRepositoryDesigner().CreateModel());
        var disabledModel = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: new PortableReadRepositoryDesigner(enabled: false).CreateModel());
        JsonSerializer.Serialize(enabledModel.Entities.Single().Queries).Should().Be(JsonSerializer.Serialize(disabledModel.Entities.Single().Queries));
        var manifest = disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().NotContain("InMemoryTestQueries").And.NotContain("strictQueries");
        disabledModel.Entities.Single().Queries.Should().OnlyContain(query => query.InMemoryQuery == null);
    }

    [Fact]
    public void PortableManifestHasPerOperationCoverageAndExplicitSemantics()
    {
        using var context = Context();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: new PortableReadRepositoryDesigner(strict: true).CreateModel());
        var root = JsonNode.Parse(RepositoryManifestRenderer.Render(model))!;
        var testDoubles = root["testDoubles"]!;
        testDoubles["mode"]!.GetValue<string>().Should().Be("PortableReadsWithScriptedOverrides");
        testDoubles["execution"]!["semantics"]!.GetValue<string>().Should().Be("LINQ-to-Objects");
        testDoubles["execution"]!["caching"]!.GetValue<string>().Should().Be("NotSimulated");
        testDoubles["execution"]!["strictQueries"]!.GetValue<bool>().Should().BeTrue();
        var operations = testDoubles["repositories"]![0]!["operations"]!.AsArray();
        operations.Should().HaveCount(model.Entities.Single().Queries.Count);
        operations.Should().OnlyContain(operation => operation!["status"]!.GetValue<string>() == "Generated" && operation["reasons"]!.AsArray().Count == 0);
        RepositoryManifestRenderer.Render(model).Should().Be(RepositoryManifestRenderer.Render(model));
    }

    [Fact]
    public void PrerequisitesAreValidatedAndPresetsKeepExplicitSelection()
    {
        using var context = Context();
        var builder = Builder();
        Entity(builder).HasQuery("Count").Count();
        builder.Configure(options => options.UseCompatibilityDefaults().UseSimpleDefaults());
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        model.Settings.InMemoryTestQueries.Should().BeTrue();
        builder.Configure(options => options.UseInMemoryTestStores(false));
        Action missingStore = () => RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        missingStore.Should().Throw<InvalidOperationException>().WithMessage("*RGD3301*");
        builder.Configure(options => options.UseInMemoryTestStores().UseInMemoryTestQueries(false).RequirePortableTestQueries());
        Action missingQueries = () => RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        missingQueries.Should().Throw<InvalidOperationException>().WithMessage("*RGD3301*");
    }

    [Theory]
    [InlineData("EF", "Microsoft.EntityFrameworkCore.EF.Property")]
    [InlineData("Navigation", "Navigation traversal")]
    [InlineData("Include", "Include/load-profile")]
    [InlineData("Custom", "PortableTestFunctions.Accept")]
    [InlineData("Isolation", "transaction/isolation")]
    [InlineData("WriteRead", "Command-side reads")]
    [InlineData("Proxy", "data-shape store")]
    public void NonPortableShapesGetReasonsInsteadOfAnEmptyImplementation(string scenario, string expected)
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        var query = entity.HasQuery("Read");
        switch (scenario)
        {
            case "EF": query.Where(row => EF.Property<int>(row, "Id") > 0).Count(); break;
            case "Navigation": query.Where(row => row.Detail != null && row.Detail.Text == "value").Count(); break;
            case "Include": query.Include(row => row.Detail).Count(); break;
            case "Custom": query.Where(row => PortableTestFunctions.Accept(row.Id)).Count(); break;
            case "Isolation": query.Select(row => row.Name).OrderBy(row => row).Paged(RepositoryPageConsistency.Serializable); break;
            case "WriteRead": entity.WriteEnabled = true; query.OnWriteRepository().Count(); break;
            case "Proxy": entity.HasInMemoryStore<PortableView, int>(row => row.Id, row => new PortableView(row.Id, row.Name, row.Category, row.Price)); query.Count(); break;
        }
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        var receipt = model.Entities.Single().Queries.Single().InMemoryQuery!;
        receipt.Supported.Should().BeFalse();
        string.Join(" ", receipt.Reasons).Should().Contain(expected);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        RepositoryManifestRenderer.Render(model).Should().Contain("RequiresOverride");
    }

    [Fact]
    public void ActiveEfFiltersAreNeverSilentlyDroppedEvenIfTheyOnlyReferenceScalars()
    {
        using var context = new PortableFilteredContext(new DbContextOptionsBuilder<PortableFilteredContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder();
        var entity = Entity(builder);
        entity.HasQuery("Filtered").Count();
        entity.HasQuery("ExplicitlyUnfiltered").IgnoreQueryFilters().Count();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        model.Entities.Single().Queries.Single(query => query.MethodName == "FilteredAsync").InMemoryQuery!.Reasons.Should().Contain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
        model.Entities.Single().Queries.Single(query => query.MethodName == "ExplicitlyUnfilteredAsync").InMemoryQuery!.Supported.Should().BeTrue();
    }

    [Fact]
    public void StrictModeRequiresExplicitAcknowledgementAndPerQueryOptOutIsReversible()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var query = Entity(builder).HasQuery("SqlRead").Where(row => EF.Property<int>(row, "Id") > 0).Count();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD3302*SqlRead*");
        query.RequireTestOverride("Covered by SQL integration tests; unit tests script this operation.");
        generate.Should().NotThrow();
        query.RequireTestOverride(null);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD3302*");
        Action blank = () => query.RequireTestOverride(" ");
        blank.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResultBuilderOverrideRespectsStaleStageProtection()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var results = Entity(builder).HasQuery("Read").Select(row => row.Name);
        var final = results.Select(row => row.Length).RequireTestOverride("Script the final result.").List();
        Action stale = () => results.RequireTestOverride("Invalid stale change");
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        Action blank = () => final.RequireTestOverride("");
        blank.Should().Throw<ArgumentException>();
        RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build()).Entities.Single().Queries.Single().InMemoryQuery!.Supported.Should().BeFalse();
    }

    [Fact]
    public void MissingStoreRequiresOverrideAndExcludedEntitiesDoNotParticipateInStrictChecking()
    {
        using var context = Context();
        var builder = Builder();
        Entity(builder).HasQuery("Count").Count();
        var other = builder.Entity<PortableDetail>();
        other.WriteEnabled = false;
        other.HasQuery("Count").Count();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        model.Entities.Single(entity => entity.EntityName == nameof(PortableDetail)).Queries.Single().InMemoryQuery!.Reasons.Should().ContainSingle().Which.Should().Contain("No HasInMemoryStore");
        other.GenerateTestDouble(false);
        builder.Configure(options => options.RequirePortableTestQueries());
        RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build()).Entities.Single(entity => entity.EntityName == nameof(PortableDetail)).Queries.Single().InMemoryQuery.Should().BeNull();
    }
}
