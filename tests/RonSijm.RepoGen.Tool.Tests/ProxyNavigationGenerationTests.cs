using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyNavigationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy reads")]
public sealed class ProxyNavigationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryLayoutLoadsMappedReadRowsOnceWithoutModelConstruction(RepositoryOutputLayout layout)
    {
        using var context = NavigationReadTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(context, new ProxyNavigationRepositoryDesigner(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("List<global::Generated.ProxyNavigation.TestDoubles.__RepoGenNavigationReadBookReadRow>")
            .And.Contain("InMemoryRelationships.LoadCollection").And.Contain("InMemoryRelationships.LoadReference")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.NavigationReadLibrary")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
        var model = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: new ProxyNavigationRepositoryDesigner().CreateModel());
        model.Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single(query => query.MethodName == "CardsAsync")
            .InMemoryQuery!.Navigation!.Roots.Should().OnlyContain(root => root.EntityMapping != null && root.StoreRowTypeName != null);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("navigationSlots").And.Contain("constructsPersistenceEntity\": false");
    }

    [Fact]
    public void DisablingDeclarationsOrBackendLeavesProductionContractsApiAndCacheShapesUnchanged()
    {
        using var context = NavigationReadTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, new ProxyNavigationRepositoryDesigner());
        var enabledModel = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: new ProxyNavigationRepositoryDesigner().CreateModel());
        foreach (var designer in new[] { new ProxyNavigationRepositoryDesigner(declarations: false), new ProxyNavigationRepositoryDesigner(enabled: false) })
        {
            var disabled = RepositoryGenerator.Generate(context, designer);
            enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents)
                .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents));
            enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
            JsonSerializer.Serialize(enabledModel.Entities.SelectMany(entity => entity.Queries)).Should().Be(JsonSerializer.Serialize(
                RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: designer.CreateModel()).Entities.SelectMany(entity => entity.Queries)));
            ScriptedTestDoubleTestHarness.Compile(disabled);
            string.Join("\n", disabled.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("InMemoryRelationships.Load");
        }
    }

    [Theory]
    [InlineData("MissingMapping", "MapInMemoryEntity")]
    [InlineData("MissingKey", "mapped scalar keys")]
    [InlineData("MissingScalar", "Price")]
    [InlineData("MissingDeclaration", "HasInMemoryNavigation")]
    [InlineData("Excluded", "enabled, selected")]
    [InlineData("Mixed", "both ends")]
    [InlineData("Identity", "identity")]
    [InlineData("Filtered", "Filtered Include")]
    public void UnsupportedGraphsAreExplicitOverridesEvenWithEmptyStores(string scenario, string expectedReason)
    {
        using var context = NavigationReadTestHarness.Context();
        var builder = Builder(strict: false); ProxyNavigationRepositoryDesigner.Stores(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        var query = library.HasQuery("Related");
        switch (scenario)
        {
            case "MissingMapping": builder.Entity<NavigationReadAuthor>().WithoutInMemoryEntityMapping(); break;
            case "MissingKey": builder.Entity<NavigationReadBook>().MapInMemoryEntity<ProxyNavigationBookData>(row => new NavigationReadBook { Id = row.Key, AuthorId = row.AuthorKey, Price = row.Price }); break;
            case "MissingScalar": builder.Entity<NavigationReadBook>().MapInMemoryEntity<ProxyNavigationBookData>(row => new NavigationReadBook { Id = row.Key, LibraryCode = row.Code, LibraryRegion = row.Region, AuthorId = row.AuthorKey }); break;
            case "MissingDeclaration": builder.Entity<NavigationReadBook>().WithoutInMemoryNavigation(row => row.Author); break;
            case "Excluded": builder.Entity<NavigationReadAuthor>().GenerateTestDouble(false); break;
            case "Mixed": library.HasInMemoryStore(row => row.Id, row => new NavigationReadLibrary { Id = row.Id, Code = row.Code, Region = row.Region }); break;
            case "Filtered": query.Load(load => load.Include(row => row.Books.Where(book => book.Price > 10))); break;
        }
        if (scenario == "Identity") query.Select(row => row.Books.Distinct().Select(book => book.Id).ToList()).List();
        else query.Select(row => row.Books.Select(book => book.Author.Name + book.Price).ToList()).List();
        var model = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: builder.Build());
        var coverage = model.Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains(expectedReason, StringComparison.OrdinalIgnoreCase));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        query.RequireTestOverride("A typed service-test script deliberately owns this unsupported graph.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public async Task PrivateRelatedModelsCompileWithNoModelAssemblyReference()
    {
        using var context = ProxyReadTestHarness.Context();
        var builder = ProxyReadTestHarness.Builder(strict: true);
        var entity = ProxyReadTestHarness.Entity(builder); entity.HasInMemoryNavigation(row => row.Department);
        var target = builder.Entity<ProxyReadDepartment>(); target.WriteEnabled = false;
        target.HasInMemoryStore<ProxyReadDepartmentData, int>(row => row.Key, row => new ProxyReadDepartmentData(row.Key, row.Caption, row.Archived))
            .MapInMemoryEntity<ProxyReadDepartmentData>(row => new ProxyReadDepartment { Id = row.Key, Name = row.Caption, Hidden = row.Archived });
        entity.HasQuery("Related").OrderBy(row => row.Id).Select(row => row.Department == null ? "none" : row.Department.Name).List();
        entity.HasQuery("Source").From(sources => sources.Set<ProxyReadEntity>().OrderBy(row => row.Id)
            .Select(row => row.Department == null ? "none" : row.Department.Name)).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
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
            """;
        var trees = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.TestDoubles or RepositoryOutputKind.Contracts)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(contracts));
        var compilation = CSharpCompilation.Create("ModelFreeRelatedRows", trees, ScriptedTestDoubleTestHarness.TestingReferences()
            .Select(path => MetadataReference.CreateFromFile(path)), ScriptedTestDoubleTestHarness.CompilerOptions());
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        var before = ProxyReadEntity.Constructions;
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadEntityRepositoryTestDouble")!, fixture)!;
        ProxyReadTestHarness.Store(repository).SeedRange(ProxyReadTestHarness.Rows());
        var data = ProxyReadTestHarness.Data(repository);
        ((RonSijm.RepoGen.Testing.InMemoryStore<ProxyReadDepartmentData, int>)data.GetType().GetProperty("ProxyReadDepartmentStore")!.GetValue(data)!)
            .SeedRange([new(1, "Office"), new(2, "Hidden", true)]);
        (await ProxyReadTestHarness.Invoke<Task<IReadOnlyList<string>>>(repository, "RelatedAsync")).Should().Equal("Office", "none", "Hidden", "Office");
        (await ProxyReadTestHarness.Invoke<Task<IReadOnlyList<string>>>(repository, "SourceAsync")).Should().Equal("Office", "none", "Hidden", "Office");
        ProxyReadEntity.Constructions.Should().Be(before);
    }
}
