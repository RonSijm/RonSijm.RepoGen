using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemorySourceQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemorySourceQueryGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void AllSourceShapesCompileAsTypedEnumerableCallsInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new PortableSourceRepositoryDesigner(), layout);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Should().OnlyContain(file =>
            !file.Contents.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) &&
            !file.Contents.Contains("System.Linq.Queryable.", StringComparison.Ordinal) && !file.Contents.Contains("RepositoryQuerySources", StringComparison.Ordinal));
    }

    [Fact]
    public void DisablingBackendChangesNoProductionContractsApiOrCacheFingerprint()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, new PortableSourceRepositoryDesigner());
        var disabled = RepositoryGenerator.Generate(context, new PortableSourceRepositoryDesigner(enabled: false));
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles)
            .Select(file => file.Contents).Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        var enabledModel = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: new PortableSourceRepositoryDesigner().CreateModel());
        var disabledModel = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: new PortableSourceRepositoryDesigner(enabled: false).CreateModel());
        JsonSerializer.Serialize(enabledModel.Entities.SelectMany(entity => entity.Queries))
            .Should().Be(JsonSerializer.Serialize(disabledModel.Entities.SelectMany(entity => entity.Queries)));
    }

    [Fact]
    public void ReceiptListsDistinctRootsAndOperatorsWithoutAddingIrrelevantOwnerOrDefaultSorts()
    {
        using var context = Context();
        var model = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: new PortableSourceRepositoryDesigner().CreateModel());
        var owner = model.Entities.Single(entity => entity.EntityName == nameof(PortableSourceOrder));
        var self = owner.Queries.Single(query => query.MethodName == "SelfAsync").InMemoryQuery!;
        self.Source!.Roots.Should().ContainSingle().Which.EntityTypeName.Should().Be(owner.EntityTypeName);
        self.Source.Operators.Should().Contain("Join");
        var customersOnly = owner.Queries.Single(query => query.MethodName == "CustomersOnlyAsync").InMemoryQuery!;
        customersOnly.Source!.Roots.Should().ContainSingle().Which.EntityTypeName.Should().EndWith("PortableSourceCustomer");
        var manifest = RepositoryManifestRenderer.Render(model);
        manifest.Should().Be(RepositoryManifestRenderer.Render(model));
        JsonNode.Parse(manifest)!["testDoubles"]!["execution"]!["reads"]!.GetValue<string>()
            .Should().Be("OneDetachedParticipatingStoreSnapshotPerInvocation");
        var receipt = JsonNode.Parse(manifest)!["testDoubles"]!["repositories"]!.AsArray()
            .Single(repository => repository!["entity"]!.GetValue<string>() == owner.EntityTypeName)!;
        receipt["operations"]!.AsArray().Should().OnlyContain(operation => operation!["status"]!.GetValue<string>() == "Generated");
        receipt["operations"]!.AsArray().First()!["sourceSnapshot"]!["capture"]!.GetValue<string>().Should().Be("DetachedCopiesUnderOneContextLock");
    }

    [Fact]
    public async Task AllSourcesResolveOneDesignedPartitionAndNeverMixRowsFromOtherKeys()
    {
        using var context = Context();
        const string consumer = """
            using System;
            using System.Threading.Tasks;
            using Generated.Sources.TestDoubles;
            using RonSijm.RepoGen.Tool.Tests;
            public static class SourcePartitionConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new PortableSourceContextRepositoryFixture();
                    fixture.For("one").PortableSourceOrderStore.Seed(new() { Id = 1, CustomerId = 1 });
                    fixture.For("two").PortableSourceOrderStore.Seed(new() { Id = 1, CustomerId = 1 });
                    fixture.For("two").PortableSourceCustomerStore.Seed(new() { Id = 1, Name = "second" });
                    var repository = new PortableSourceOrderRepositoryTestDouble(fixture);
                    if ((await repository.InnerAsync(new("one"))).Count != 0) return false;
                    var second = await repository.InnerAsync(new("two"));
                    return second.Count == 1 && second[0].Customer == "second";
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new PortableSourceRepositoryDesigner(keyed: true)), consumer);
        (await (Task<bool>)assembly.GetType("SourcePartitionConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Disabled")]
    [InlineData("Excluded")]
    [InlineData("Proxy")]
    public void EveryRootNeedsASelectedEntityShapedStoreAndStrictModeRejectsOtherwise(string scenario)
    {
        using var context = Context();
        var builder = Builder();
        var owner = builder.Entity<PortableSourceOrder>();
        owner.WriteEnabled = false;
        var customer = builder.Entity<PortableSourceCustomer>();
        customer.WriteEnabled = false;
        customer.HasInMemoryStore(row => row.Id, row => new PortableSourceCustomer { Id = row.Id, Name = row.Name });
        if (scenario == "Missing") customer.WithoutInMemoryStore();
        if (scenario == "Disabled") customer.GenerateTestDouble(false);
        if (scenario == "Excluded") customer.RepositoryEnabled = false;
        if (scenario == "Proxy") customer.HasInMemoryStore<PortableSourceView, int>(row => row.OrderId, row => new PortableSourceView(row.OrderId, row.Customer, row.Amount));
        // Keep one independently selected store so global store prerequisites are satisfied.
        owner.HasInMemoryStore(row => row.Id, row => new PortableSourceOrder { Id = row.Id });
        owner.HasQuery("Names").From(sources => sources.Set<PortableSourceCustomer>().Select(row => row.Name)).List();
        var model = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: builder.Build());
        var query = model.Entities.Single(entity => entity.EntityName == nameof(PortableSourceOrder)).Queries.Single();
        query.InMemoryQuery!.Supported.Should().BeFalse();
        query.InMemoryQuery.Reasons.Should().Contain(reason => reason.Contains("Source '", StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("*RGD3302*Names*");
    }

    [Fact]
    public async Task ReplacementRootDoesNotRequireUnusedOwnersStore()
    {
        using var context = Context();
        var builder = Builder();
        var owner = builder.Entity<PortableSourceOrder>();
        owner.WriteEnabled = false;
        var customer = builder.Entity<PortableSourceCustomer>();
        customer.WriteEnabled = false;
        customer.HasInMemoryStore(row => row.Id, row => new PortableSourceCustomer { Id = row.Id, Name = row.Name });
        owner.HasVisibilityPolicy("IrrelevantOwner", row => false);
        owner.HasDefaultSort(row => row.Amount);
        owner.HasQuery("Names").From(sources => sources.Set<PortableSourceCustomer>().Select(row => row.Name)).OrderBy(name => name).List();
        builder.Configure(options => options.RequirePortableTestQueries());
        const string consumer = """
            using System.Threading.Tasks;
            using Generated.Sources.TestDoubles;
            public static class SourceOnlyConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new PortableSourceContextRepositoryFixture();
                    fixture.Data.PortableSourceCustomerStore.Seed(new() { Id = 1, Name = "included" });
                    var result = await new PortableSourceOrderRepositoryTestDouble(fixture).NamesAsync();
                    return result.Count == 1 && result[0] == "included";
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()), consumer);
        (await (Task<bool>)assembly.GetType("SourceOnlyConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderMethodsAndNonWhitelistedOperatorsRemainExplicitlyUnsupported(bool provider)
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<PortableSourceCustomer>();
        entity.WriteEnabled = false;
        entity.HasInMemoryStore(row => row.Id, row => new PortableSourceCustomer { Id = row.Id, Name = row.Name });
        var query = entity.HasQuery("Read");
        if (provider) query.From(sources => sources.Set<PortableSourceCustomer>().Where(row => EF.Property<int>(row, "Id") > 0).Select(row => row.Name)).List();
        else query.From(sources => sources.Set<PortableSourceCustomer>().Select(row => row.Name).Append("extra")).List();
        var model = RepositoryModelFactory.Create(context, typeof(PortableSourceOrder).Assembly, design: builder.Build());
        var coverage = model.Entities.Single().Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse();
        string.Join(" ", coverage.Reasons).Should().Contain(provider ? "EF.Property" : "Queryable.Append");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestQueries());
        query.RequireTestOverride("Database-only operation tested by provider fixtures.");
        RepositoryGenerator.Generate(context, builder.Build());
    }

    [Fact]
    public void EfQueryFilterOnAReferencedNonOwnerRootIsNotSilentlyDropped()
    {
        using var context = new PortableFilteredContext(new DbContextOptionsBuilder<PortableFilteredContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = InMemoryQueryTestHarness.Builder();
        var row = InMemoryQueryTestHarness.Entity(builder);
        var owner = builder.Entity<PortableDetail>();
        owner.WriteEnabled = false;
        owner.HasQuery("FilteredSource").From(sources => sources.Set<PortableRow>().Select(row => row.Id)).List();
        owner.HasQuery("UnfilteredSource").IgnoreQueryFilters().From(sources => sources.Set<PortableRow>().Select(row => row.Id)).List();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: builder.Build());
        var queries = model.Entities.Single(entity => entity.EntityName == nameof(PortableDetail)).Queries;
        queries.Single(query => query.MethodName == "FilteredSourceAsync").InMemoryQuery!.Reasons.Should().Contain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
        queries.Single(query => query.MethodName == "UnfilteredSourceAsync").InMemoryQuery!.Supported.Should().BeTrue();
    }

    private static RepositoryModelBuilder Builder() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.Sources.Contracts").UseGeneratedNamespace("Generated.Sources.Persistence")
        .UseScriptedTestDoubles("Generated.Sources.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().GenerateOnlyConfiguredEntities());
}
