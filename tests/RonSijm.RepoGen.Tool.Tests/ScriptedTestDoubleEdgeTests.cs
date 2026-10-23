using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Validation;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScriptedTestDoubleEdgeTests
{
    [Fact]
    public void AmbiguousFriendlyOverloadNamesUseStableSignatureHashes()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        var row = builder.Entity<ScriptedRow>();
        row.HasQuery("Probe").Where<FirstScriptedInputs.Input>((entity, input) => entity.Id == input.Id).Count();
        row.HasQuery("Probe").Where<SecondScriptedInputs.Input>((entity, input) => entity.Id == input.Id).Count();
        var model = RepositoryModelFactory.Create(context, typeof(ScriptedContext).Assembly, design: builder.Build());
        var operations = ScriptedTestDoubleSourceRenderer.Operations(model, model.Entities[0]);
        operations.Should().HaveCount(2);
        operations.Select(operation => operation.ScriptName).Should().OnlyHaveUniqueItems().And.OnlyContain(name => name.StartsWith("OnProbeAsync_", StringComparison.Ordinal));
        var bundle = RepositoryGenerator.Generate(model);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Should().Equal(RepositoryGenerator.Generate(model).Files);
    }

    [Fact]
    public void ArgumentPropertiesHandleCaseObjectAndEnclosingTypeCollisions()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        var row = builder.Entity<ScriptedRow>();
        row.BindReadPort<IScriptedArgumentCasePort>();
        row.HasQuery("Cases").Where<int, int>((entity, first, second) => entity.Id == first || entity.Id == second).Count();
        row.HasQuery("Objects").Where<int, int>((entity, toString, getHashCode) => entity.Id == toString || entity.Id == getHashCode).Count();
        row.HasQuery("Snapshot").Where<int>((entity, onSnapshotAsyncArguments) => entity.Id == onSnapshotAsyncArguments).Count();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var source = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents;
        source.Should().Contain(" Id_1 { get; }").And.Contain("new global::System.Int32 ToString { get; }")
            .And.Contain("new global::System.Int32 GetHashCode { get; }").And.Contain(" OnSnapshotAsyncArguments_0 { get; }");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void MultipleSelectedEntitiesRegisterWithoutGeneratingExcludedFixtures()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        builder.Entity<ScriptedRow>().HasQuery("Count").Count();
        builder.Entity<ScriptedOtherRow>().HasQuery("Count").Count();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), RepositoryOutputLayout.PerContract);
        bundle.Files.Count(file => file.Kind == RepositoryOutputKind.TestDoubles).Should().Be(3);
        var registration = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles && file.Name.Contains("Registration", StringComparison.Ordinal)).Contents;
        registration.Should().Contain("IScriptedRowRepositoryTestDouble").And.Contain("IScriptedOtherRowRepositoryTestDouble");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Theory]
    [InlineData("Invalid-Namespace")]
    [InlineData("RonSijm.RepoGen.Testing")]
    [InlineData("RonSijm.RepoGen.Testing.Consumer")]
    public void InvalidOrReservedTestingNamespacesAreRejected(string ns)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var builder = ScriptedTestDoubleTestHarness.Builder().Configure(options => options.UseScriptedTestDoubles(ns));
        builder.Entity<ScriptedRow>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1801:*");
    }

    [Fact]
    public void TestingTypeCollisionsBetweenContextsAreCaughtByBatchInventory()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var original = ScriptedTestDoubleTestHarness.Model(context, ports: false);
        var second = original with
        {
            ContextTypeName = "global::Other.ScriptedContext",
            Settings = original.Settings with { GeneratedNamespace = "Other.Persistence", ContractNamespace = "Other.Contracts" },
            Entities = original.Entities.Select(entity => entity with
            {
                Namespace = "Other.Persistence",
                Projections = entity.Projections.Select(projection => projection with { Namespace = "Other.Persistence" }).ToArray(),
                Queries = entity.Queries.Select(query => query with { Namespace = "Other.Persistence" }).ToArray()
            }).ToArray()
        };
        Action validate = () => RepositoryGeneratedTypes.Validate([original, second]);
        validate.Should().Throw<InvalidOperationException>().WithMessage("RGG0007:*");
        second = second with { Settings = second.Settings with { ScriptedTestDoubleNamespace = "Other.TestDoubles" } };
        RepositoryGeneratedTypes.Validate([original, second]);
    }

    [Fact]
    public void ContractsAndTestDoublesCompileWithOnlyEfFreeManualInputsAndRuntimeReferences()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context));
        const string manualContracts = """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            namespace RonSijm.RepoGen.Tool.Tests
            {
                public sealed record ScriptedView(int Id, string? Name);
                public sealed record ScriptedSearch(IReadOnlyList<int> Ids);
                public interface IScriptedReadPort
                {
                    Task<ScriptedView?> FindAsync(int identifier = 0, CancellationToken token = default);
                    Task<ScriptedView?> FindAsync(string text, CancellationToken token = default);
                    Task<ScriptedView?> OptionalLookupAsync(string? text = null, CancellationToken token = default);
                    Task<IReadOnlyList<ScriptedView>> SearchAsync(ScriptedSearch request, CancellationToken token = default);
                    IAsyncEnumerable<ScriptedView> StreamAsync(CancellationToken token = default);
                    async Task<bool> ExistsAsync(int id, CancellationToken token = default) => await FindAsync(id, token) is not null;
                }
                public interface IScriptedWritePort { Task<int> DeleteAsync(int identifier, CancellationToken token = default); }
            }
            """;
        var references = ScriptedTestDoubleTestHarness.TestingReferences();
        references.Select(Path.GetFileName).Should().NotContain(name => name!.Contains("EntityFramework", StringComparison.Ordinal))
            .And.NotContain(Path.GetFileName(typeof(ScriptedRow).Assembly.Location));
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.TestDoubles or RepositoryOutputKind.Contracts)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(manualContracts));
        var compilation = CSharpCompilation.Create("IndependentScriptedConsumer", sources,
            references.Select(static path => MetadataReference.CreateFromFile(path)), ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        compilation.Emit(output).Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void OptionalCompilerChecksTheTestingStageAndAllGeneratedImplementationFiles(RepositoryOutputLayout layout)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context), layout);
        var testingReferences = ScriptedTestDoubleTestHarness.TestingReferences().Append(typeof(ScriptedView).Assembly.Location);
        var before = bundle.Files.ToArray();
        var report = bundle.ValidateCompilation(new(CompilationValidationTests.References(), languageVersion: "12", warningsAsErrors: true,
            testDoubleReferences: testingReferences));
        report.Diagnostics.Should().BeEmpty();
        report.Succeeded.Should().BeTrue();
        report.TestDoublesChecked.Should().BeTrue();
        report.ContractsChecked.Should().BeFalse();
        bundle.Files.Should().Equal(before);
    }

    [Fact]
    public void MissingOptionalRuntimeProducesCompilerErrorsAtTheTestingStage()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context));
        var testingReferences = ScriptedTestDoubleTestHarness.TestingReferences().Where(path => Path.GetFileNameWithoutExtension(path) != "RonSijm.RepoGen.Testing")
            .Append(typeof(ScriptedView).Assembly.Location);
        var report = bundle.ValidateCompilation(new(CompilationValidationTests.References(), testDoubleReferences: testingReferences));
        report.TestDoublesChecked.Should().BeTrue();
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Stage == RepositoryCompilationStage.TestDoubles && diagnostic.Severity == RepositoryCompilationSeverity.Error);
    }

    [Fact]
    public void NoTestOutputNeverClaimsAnIndependentTestingCompilation()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context, enabled: false));
        var report = bundle.ValidateCompilation(new(CompilationValidationTests.References(), testDoubleReferences: []));
        report.TestDoublesChecked.Should().BeFalse();
        report.Succeeded.Should().BeTrue();
    }
}
