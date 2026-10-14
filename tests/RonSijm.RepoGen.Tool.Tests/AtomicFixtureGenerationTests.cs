using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFixtureTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void OptionalImmediateInsertionCompilesAllLayoutsAndLeavesProductionAndApiUnchanged(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped: true).Build(), layout);
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped: true, simulate: false, strict: false).Build(), layout);
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(enabled);
        using var receipt = JsonDocument.Parse(enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var operations = receipt.RootElement.GetProperty("testDoubles").GetProperty("repositories")[0].GetProperty("operations").EnumerateArray();
        var atomic = operations.Where(operation => operation.TryGetProperty("atomicSimulation", out _)).Select(operation => operation.GetProperty("atomicSimulation")).ToArray();
        atomic.Should().HaveCount(3); atomic.Should().OnlyContain(item => !item.GetProperty("relationalEquivalence").GetBoolean());
        enabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("TrackedCreateSavedAndReservedImmediateInsertSavedOnly");
        disabled.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("atomicSimulation");
    }

    [Fact]
    public void MissingOptInAndFactoriesStayStrictlyUncoveredButExplicitScriptsAreAllowed()
    {
        using var context = Context();
        Action noOptIn = () => RepositoryGenerator.Generate(context, Builder(simulate: false).Build());
        noOptIn.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*SimulateImmediateWrites*");
        Action noFactory = () => RepositoryGenerator.Generate(context, Builder(factories: false).Build());
        noFactory.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*generated values*");
        var bundle = RepositoryGenerator.Generate(context, Builder(overrideAtomic: true).Build());
        ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride").And.NotContain("atomicSimulation");
    }

    [Theory]
    [InlineData("OmittedColumn")]
    [InlineData("Tracked")]
    [InlineData("Writer")]
    public void SimulationNeverInfersDatabaseDefaultsOrBypassesProductionRequirements(string scenario)
    {
        using var context = Context(); var builder = Builder(); var entity = builder.Entity<ImmediateRow>();
        if (scenario == "Writer") builder.Configure(options => options.UseAtomicWrites<RonSijm.RepoGen.Sqlite.SqliteAtomicWriter>(false));
        else if (scenario == "Tracked") entity.HasCommand("Invalid").TrackedDelete().Stage().SimulateImmediateWrites();
        else entity.HasCommand("Invalid").Create<AtomicFixtureInput>(request => new ImmediateRow { OwnerId = request.OwnerId, Code = request.Code })
            .InsertIfAbsent(row => new { row.OwnerId, row.Code }).SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
        if (scenario == "OmittedColumn") generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*explicitly assign scalar*");
    }

    [Theory]
    [InlineData("Writes")]
    [InlineData("Entity")]
    [InlineData("Reads")]
    public void BackendSwitchesRemainIndependentAndOptional(string disabled)
    {
        using var context = Context(); var builder = Builder(mapped: true);
        if (disabled == "Writes") builder.Configure(options => options.UseInMemoryTestCommands(false).RequirePortableTestCommands(false));
        if (disabled == "Entity") builder.Entity<ImmediateRow>().WriteEnabled = false;
        if (disabled == "Reads") builder.Configure(options => options.UseInMemoryTestQueries(false));
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents))
            .Contains("ExecuteInsertOrUpdateAsync<", StringComparison.Ordinal).Should().Be(disabled == "Reads");
    }

    [Fact]
    public void PublicMappedInsertionsCompileWithoutModelEfOrProviderAssemblies()
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, Builder(mapped: true).Build());
        const string publicTypes = """
            namespace RonSijm.RepoGen.Tool.Tests;
            public sealed record AtomicFixtureInput(int OwnerId, string Code = "new", int Amount = 2, int ExpectedVersion = 1, byte[]? Stamp = null, int Id = 0, bool Hidden = false, int Minimum = 0, string Partition = "A");
            public sealed record ImmediateData(int Key, int OwnerId, string Code, int Amount, int Previous, bool Hidden, int Revision, byte[] Marker, string Tag = "arranged");
            public sealed record ImmediateView(int Id, string Code, int Amount, int Previous, int Version, byte[] Stamp);
            """;
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.TestDoubles)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(publicTypes));
        var compilation = CSharpCompilation.Create("ModelFreeAtomicFixture", sources,
            ScriptedTestDoubleTestHarness.TestingReferences().Select(path => MetadataReference.CreateFromFile(path)), ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }
}
