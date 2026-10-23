using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy reads")]
public sealed class ProxyReadGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void MappedPrivateModelsCompileInEveryLayoutWithoutEntityConstruction(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("internal sealed class __RepoGenProxyReadEntityReadRow")
            .And.Contain("public required global::System.Int32 Score")
            .And.Contain("row.RawScore * 2")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.ProxyReadEntity(")
            .And.NotContain("IEnumerable<global::RonSijm.RepoGen.Tool.Tests.ProxyReadEntity>")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable.");
        source.Split("internal sealed class __RepoGenProxyReadEntityReadRow", StringSplitOptions.None).Should().HaveCount(2);
    }

    [Fact]
    public void UnchangedProductionContractsApiAndQueryCacheShapeWithMappingOffOrBackendOff()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner());
        foreach (var designer in new[] { new ProxyReadRepositoryDesigner(mapping: false), new ProxyReadRepositoryDesigner(enabled: false) })
        {
            var disabled = RepositoryGenerator.Generate(context, designer);
            enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
                .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
            enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
            var enabledModel = Model(context, new ProxyReadRepositoryDesigner());
            var disabledModel = Model(context, designer);
            JsonSerializer.Serialize(enabledModel.Entities.SelectMany(entity => entity.Queries))
                .Should().Be(JsonSerializer.Serialize(disabledModel.Entities.SelectMany(entity => entity.Queries)));
            string.Join("\n", disabled.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("__RepoGenProxyReadEntityReadRow");
        }
    }

    [Fact]
    public void TestOutputCompilesWithoutEvenAReferenceToTheAssemblyContainingTheModel()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new ProxyReadRepositoryDesigner());
        const string contracts = """
            namespace RonSijm.RepoGen.Tool.Tests;
            public sealed class ProxyReadData
            {
                public int Key { get; set; } public string Caption { get; set; } = "";
                public string Group { get; set; } = ""; public int RawScore { get; set; }
                public bool Archived { get; set; } public int DepartmentKey { get; set; }
                public byte[] Stamp { get; set; } = [];
            }
            public sealed record ProxyReadDepartmentData(int Key, string Caption, bool Archived = false);
            public sealed record ProxyReadView(int Id, string Name, string Category, int Score, string? Department)
            { public ProxyReadView() : this(0, "", "", 0, null) { } }
            public sealed record ProxyReadTotal(string Category, int Count, int Score)
            { public ProxyReadTotal() : this("", 0, 0) { } }
            """;
        var trees = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.TestDoubles or RepositoryOutputKind.Contracts)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(contracts));
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ModelFreeProxyConsumer", trees, references, ScriptedTestDoubleTestHarness.CompilerOptions());
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Predicate")]
    [InlineData("Projection")]
    [InlineData("DefaultSort")]
    [InlineData("Visibility")]
    [InlineData("DynamicSort")]
    [InlineData("Cursor")]
    [InlineData("Group")]
    [InlineData("Scalar")]
    [InlineData("Dictionary")]
    [InlineData("Source")]
    public void MissingScalarCoverageIsAnOverrideNotADefaultValue(string scenario)
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        var query = entity.HasQuery("Missing");
        switch (scenario)
        {
            case "Predicate": query.Where(row => row.Unmapped > 0).Count(); break;
            case "Projection": query.Select(row => row.Unmapped).List(); break;
            case "DefaultSort": entity.HasDefaultSort(row => row.Unmapped); query.Count(); break;
            case "Visibility": entity.HasVisibilityPolicy("Missing", row => row.Unmapped > 0); query.Count(); break;
            case "DynamicSort": query.AllowSorting(row => row.Unmapped).Select(row => row.Id).List(); break;
            case "Cursor": query.ProjectTo(row => row.Id).CursorPaged<int>((row, cursor) => row.Unmapped > cursor, row => row.Id); break;
            case "Group": query.GroupBy(row => row.Unmapped, group => group.Count()).List(); break;
            case "Scalar": query.Results<ProxyReadEntity>().Sum(row => row.Unmapped); break;
            case "Dictionary": query.Results<ProxyReadEntity>().Dictionary(row => row.Id, row => row.Unmapped); break;
            case "Source": query.From(sources => sources.Set<ProxyReadEntity>().Select(row => row.Unmapped)).List(); break;
        }
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: builder.Build());
        var coverage = model.Entities.Single().Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse();
        coverage.Reasons.Should().Contain(reason => reason.Contains("Unmapped", StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Missing*Unmapped*");
        query.RequireTestOverride("Deliberate provider-only field.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Theory]
    [InlineData("Navigation")]
    [InlineData("Computed")]
    [InlineData("Constructor")]
    [InlineData("Method")]
    [InlineData("Identity")]
    [InlineData("Cast")]
    [InlineData("TypeCheck")]
    [InlineData("EntityDistinct")]
    public void EntityBehaviorOrIdentityCannotMasqueradeAsScalarAdaptation(string scenario)
    {
        using var context = Context();
        var builder = Builder();
        var query = Entity(builder).HasQuery("NotPortable");
        switch (scenario)
        {
            case "Navigation": query.Select(row => row.Department == null ? "" : row.Department.Name).List(); break;
            case "Computed": query.Select(row => row.Computed).List(); break;
            case "Constructor": query.Select(row => new ProxyReadEntity { Id = row.Id }).Select(row => row.Id).List(); break;
            case "Method": query.Select(row => row.ToString()).List(); break;
            case "Identity": query.From(sources => sources.Set<ProxyReadEntity>().Select(row => row).Select(row => row.Id)).List(); break;
            case "Cast": query.From(sources => sources.Set<ProxyReadEntity>().Select(row => (object)row).Select(row => row.ToString())).List(); break;
            case "TypeCheck": query.Where(row => row is ProxyReadEntity).Count(); break;
            case "EntityDistinct": query.From(sources => sources.Set<ProxyReadEntity>().Distinct().Select(row => row.Id)).List(); break;
        }
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: builder.Build());
        model.Entities.Single().Queries.Single().InMemoryQuery!.Supported.Should().BeFalse();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Theory]
    [InlineData("Factory")]
    [InlineData("ConstructorArgument")]
    [InlineData("Navigation")]
    [InlineData("Computed")]
    [InlineData("AmbientState")]
    public void InvalidMappingDeclarationsHaveActionableDiagnostics(string scenario)
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        entity.HasQuery("Count").Count();
        switch (scenario)
        {
            case "Factory": entity.MapInMemoryEntity<ProxyReadData>(row => ProxyReadEntityFactory(row.Key)); break;
            case "ConstructorArgument": entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity()); break;
            case "Navigation": entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key, Department = new ProxyReadDepartment() }); break;
            case "Computed":
                var input = Expression.Parameter(typeof(ProxyReadData), "row");
                var invalid = Expression.MemberInit(Expression.New(typeof(ProxyReadEntity)), Expression.Bind(typeof(ProxyReadEntity).GetProperty(nameof(ProxyReadEntity.Computed))!, Expression.Constant("value")));
                entity.MapInMemoryEntity(Expression.Lambda<Func<ProxyReadData, ProxyReadEntity>>(invalid, input));
                break;
            case "AmbientState": entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key, Score = DateTime.Now.Day }); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*");
    }

    [Fact]
    public void MappingRequiresMatchingProxyAndCanBeRemovedOrReplaced()
    {
        var builder = Builder();
        var entity = builder.Entity<ProxyReadEntity>();
        Action noStore = () => entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key });
        noStore.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*");
        Entity(builder);
        Action mismatch = () => entity.MapInMemoryEntity<ProxyReadDepartmentData>(row => new ProxyReadEntity { Id = row.Key });
        mismatch.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*");
        entity.WithoutInMemoryEntityMapping();
        builder.Build().Entities.Single().InMemoryStore!.EntityMapping.Should().BeNull();
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key });
        entity.HasInMemoryStore<ProxyReadData, int>(row => row.Key, row => new ProxyReadData { Key = row.Key });
        builder.Build().Entities.Single().InMemoryStore!.EntityMapping.Should().BeNull("a replacement store must never retain a stale adapter");
    }

    [Fact]
    public void ReceiptExplainsScalarMappingsAndStrictCoverageDeterministically()
    {
        using var context = Context();
        var model = Model(context, new ProxyReadRepositoryDesigner());
        var manifest = RepositoryManifestRenderer.Render(model);
        manifest.Should().Be(RepositoryManifestRenderer.Render(model));
        using var json = JsonDocument.Parse(manifest);
        var receipt = json.RootElement.GetProperty("testDoubles").GetProperty("stores").GetProperty("entities")[1].GetProperty("readMapping");
        receipt.GetProperty("constructsPersistenceEntity").GetBoolean().Should().BeFalse();
        receipt.GetProperty("writes").GetString().Should().Be("NotAdapted");
        receipt.GetProperty("members").EnumerateArray().Select(item => item.GetString()).Should().Contain("Id").And.NotContain("Unmapped");
        model.Entities.Single(entity => entity.EntityName == nameof(ProxyReadEntity)).Queries.Should()
            .OnlyContain(query => query.InMemoryQuery!.Supported == (query.MethodName != "MissingAsync"));
    }

    private static ProxyReadEntity ProxyReadEntityFactory(int id) => new() { Id = id };
    private static RepositoryGenerationModel Model(ProxyReadContext context, RepositoryDesigner designer) =>
        RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: designer.CreateModel());
}
