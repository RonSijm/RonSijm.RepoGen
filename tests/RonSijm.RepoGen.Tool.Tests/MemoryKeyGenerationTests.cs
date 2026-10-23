using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Memory key comparers")]
public sealed class MemoryKeyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExplicitComparerCompilesWithAnHonestReceiptInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = Context(); var builder = Builder();
        builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().Contain("keyComparer: global::System.StringComparer.OrdinalIgnoreCase");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        using var json = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var receipt = json.RootElement.GetProperty("testDoubles").GetProperty("stores").GetProperty("entities")[0].GetProperty("keyComparer");
        receipt.GetProperty("scope").GetString().Should().Be("FixtureKeyIdentityOnly");
        receipt.GetProperty("evaluation").GetString().Should().Be("OncePerStorePerContextPartition");
        foreach (var property in new[] { "queryPredicatesChanged", "relationshipMatchingChanged", "contextKeyComparisonChanged", "relationalEquivalence" })
            receipt.GetProperty(property).GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void DefaultOutputAndProductionContractsDoNotChange()
    {
        using var context = Context(); var builder = Builder(); var baseline = RepositoryGenerator.Generate(context, builder.Build());
        builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var configured = RepositoryGenerator.Generate(context, builder.Build());
        configured.Files.Where(file => file.Kind is not (RepositoryOutputKind.TestDoubles or RepositoryOutputKind.Manifest)).Should()
            .BeEquivalentTo(baseline.Files.Where(file => file.Kind is not (RepositoryOutputKind.TestDoubles or RepositoryOutputKind.Manifest)));
        configured.CreateApiSnapshot().ToJson().Should().Be(baseline.CreateApiSnapshot().ToJson());
        builder.Entity<MemoryKeyRow>().WithoutInMemoryKeyComparer(); RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(baseline.Files);
        baseline.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("keyComparer:");
        baseline.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("keyComparer");
    }

    [Fact]
    public void DeclarationRequiresAnExactStoreKeyAndRemovalKeepsOtherAdapters()
    {
        var builder = new RepositoryModelBuilder(); var entity = builder.Entity<MemoryKeyRow>();
        Action missing = () => entity.HasInMemoryKeyComparer<string>(() => StringComparer.Ordinal); missing.Should().Throw<InvalidOperationException>().WithMessage("RGD3204:*");
        Action nullFactory = () => entity.HasInMemoryKeyComparer<string>(null!); nullFactory.Should().Throw<ArgumentNullException>();
        builder = Builder(); entity = builder.Entity<MemoryKeyRow>();
        Action mismatch = () => entity.HasInMemoryKeyComparer<int>(() => EqualityComparer<int>.Default); mismatch.Should().Throw<InvalidOperationException>().WithMessage("RGD3204:*");
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase).WithoutInMemoryKeyComparer();
        var store = builder.Build().Entities.Single().InMemoryStore!;
        store.KeyComparer.Should().BeNull(); store.EntityMapping.Should().NotBeNull(); store.WriteMapping.Should().NotBeNull();
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase).WithoutInMemoryEntityMapping();
        builder.Build().Entities.Single().InMemoryStore!.KeyComparer.Should().NotBeNull();
        entity.HasInMemoryStore<MemoryKeyData, string>(row => row.Key, row => new(row.Key, row.Caption, row.Revision, row.Extra));
        builder.Build().Entities.Single().InMemoryStore!.KeyComparer.Should().BeNull();
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase).WithoutInMemoryStore();
        builder.Build().Entities.Single().InMemoryStore.Should().BeNull();
    }

    [Theory]
    [InlineData("Captured")]
    [InlineData("Culture")]
    [InlineData("StaticFactory")]
    [InlineData("Method")]
    [InlineData("Private")]
    [InlineData("Entity")]
    [InlineData("GenericInfrastructure")]
    [InlineData("DerivedInfrastructure")]
    [InlineData("ObjectCast")]
    [InlineData("Null")]
    public void InvalidSelectedFactoriesFailWithoutExecutingThem(string scenario)
    {
        using var context = Context(); var builder = Builder();
        IEqualityComparer<string> captured = StringComparer.OrdinalIgnoreCase;
        Expression<Func<IEqualityComparer<string>>> factory = scenario switch
        {
            "Captured" => () => captured,
            "Culture" => () => StringComparer.CurrentCultureIgnoreCase,
            "StaticFactory" => () => ThrowingFactory.Comparer,
            "Method" => () => StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, true),
            "Private" => () => new HiddenComparer(),
            "Entity" => () => new MemoryKeyEntityComparer(new MemoryKeyRow()),
            "GenericInfrastructure" => () => new MemoryKeyInfrastructureComparer(new List<Microsoft.EntityFrameworkCore.DbContext>()),
            "DerivedInfrastructure" => () => new MemoryKeyDerivedInfrastructureComparer(null),
            "ObjectCast" => () => (IEqualityComparer<string>)(object)new object(),
            _ => () => null!
        };
        builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer(factory);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3204:*");
    }

    [Theory]
    [InlineData("Stores")]
    [InlineData("Doubles")]
    [InlineData("Entity")]
    public void UnusedFactoriesAreNotValidatedOrEmitted(string scenario)
    {
        using var context = Context(); var builder = Builder(); var entity = builder.Entity<MemoryKeyRow>();
        entity.HasInMemoryKeyComparer<string>(() => ThrowingFactory.Comparer);
        if (scenario == "Stores") builder.Configure(options => options.RequirePortableTestCommands(false).UseInMemoryTestCommands(false).UseInMemoryTestQueries(false).UseInMemoryTestStores(false));
        if (scenario == "Doubles") builder.Configure(options => options.RequirePortableTestCommands(false).UseInMemoryTestCommands(false).UseInMemoryTestQueries(false).UseInMemoryTestStores(false).UseScriptedTestDoubles(null));
        if (scenario == "Entity")
        {
            entity.GenerateTestDouble(false);
            builder.Entity<MemoryKeyCompoundRow>().HasInMemoryStore(row => new ValueTuple<string, int>(row.Code, row.Slot),
                row => new MemoryKeyCompoundRow { Code = row.Code, Slot = row.Slot, Title = row.Title }).HasQuery("Count").Count();
        }
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().OnlyContain(file => !file.Contents.Contains("keyComparer:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuiltInDefaultAndOrdinalComparersAreExplicitlyAllowed()
    {
        using var context = Context(); var builder = Builder(); var entity = builder.Entity<MemoryKeyRow>();
        entity.HasInMemoryKeyComparer<string>(() => EqualityComparer<string>.Default); ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.Ordinal); ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void PublicScalarMappedComparerOutputCompilesWithoutTheModelAssembly()
    {
        using var context = Context(); var builder = Builder(); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        const string contracts = """
            namespace RonSijm.RepoGen.Tool.Tests;
            public sealed record MemoryKeyData(string Key, string Caption, int Revision = 0, string Extra = "arranged");
            public sealed record MemoryKeyRequest(string Key, string Title = "", string Context = "A");
            """;
        var trees = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.TestDoubles)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(contracts));
        var compilation = CSharpCompilation.Create("ModelFreeMemoryKeys", trees,
            ScriptedTestDoubleTestHarness.TestingReferences().Select(path => MetadataReference.CreateFromFile(path)), ScriptedTestDoubleTestHarness.CompilerOptions());
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void PublicTypesCannotEmitInaccessibleConstructors()
    {
        using var context = Context(); var builder = Builder();
        var constructor = typeof(PrivateConstructorComparer).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [])!;
        var factory = Expression.Lambda<Func<IEqualityComparer<string>>>(Expression.New(constructor));
        builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer(factory);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3204:*public constructors*");
    }

    public sealed class PrivateConstructorComparer : IEqualityComparer<string>
    {
        private PrivateConstructorComparer() { }
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
    }

    private sealed class HiddenComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
    }
    private static class ThrowingFactory
    {
        public static IEqualityComparer<string> Comparer => throw new InvalidOperationException("Must not run during generation.");
    }
}
