using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Validation;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompilationValidationTests
{
    [Theory]
    [InlineData("compatibility", RepositoryOutputLayout.SingleFile)]
    [InlineData("compatibility", RepositoryOutputLayout.PerEntity)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    public void OptionalCompilerValidatesEverySurfaceAndLayoutWithoutOutputChanges(string mode, RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = Bundle(context, mode, layout);
        var before = bundle.Files.ToArray();
        var report = bundle.ValidateCompilation(new(References(), mode == "closed" ? ContractReferences() : null, warningsAsErrors: true));
        report.Diagnostics.Should().BeEmpty();
        report.Succeeded.Should().BeTrue();
        report.ContractsChecked.Should().Be(mode == "closed");
        bundle.Files.Should().Equal(before);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void ExplicitContractReferencesCompileThePublicApiWithoutEfOrRuntimeProviders()
    {
        using var context = Context();
        var references = ContractReferences();
        references.Select(Path.GetFileName).Should().NotContain(name => name!.Contains("EntityFrameworkCore", StringComparison.Ordinal));
        var report = Bundle(context).ValidateCompilation(new(References(), references));
        report.ContractsChecked.Should().BeTrue();
        report.Succeeded.Should().BeTrue();
        report.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("closed", "Compiled.Contracts.IReadClosedRowRepository")]
    [InlineData("cqrs", "Compiled.Persistence.IReadClosedRowRepository")]
    public void ConsumerCompilationProvesReadPortsCannotSave(string mode, string port)
    {
        using var context = Context();
        var source = new RepositoryCompilationSource("ReadConsumer.cs", $"public static class ReadConsumer {{ public static void Run({port} repository) {{ _ = repository.SaveChangesAsync(); }} }}");
        var report = Bundle(context, mode).ValidateCompilation(new(References(), additionalSources: [source]));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().ContainSingle().Which.Should().Match<RepositoryCompilationDiagnostic>(diagnostic =>
            diagnostic.Id == "CS1061" && diagnostic.File == source.Name && diagnostic.Line == 1 && diagnostic.Column > 0);
        Action fail = report.ThrowIfFailed;
        fail.Should().Throw<RepositoryCompilationException>().Which.Report.Should().BeSameAs(report);
    }

    [Fact]
    public void ConsumerSyntaxFailuresKeepOriginalCompilerIdAndOneBasedLocation()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), additionalSources: [new("Broken.cs", "public class Broken {\n  public int Value = ;\n}")]));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS1525" && diagnostic.File == "Broken.cs" && diagnostic.Line == 2 && diagnostic.Column > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarningsAreVisibleAndOnlyBlockWhenExplicitlyConfigured(bool warningsAsErrors)
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), additionalSources: [new("Warnings.cs", "#nullable enable\npublic class WarningDto { public string Value { get; } = null; }")], warningsAsErrors: warningsAsErrors));
        report.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("CS8625");
        report.Diagnostics[0].Severity.Should().Be(RepositoryCompilationSeverity.Warning);
        report.Succeeded.Should().Be(!warningsAsErrors);
        if (warningsAsErrors)
        {
            Action fail = report.ThrowIfFailed;
            fail.Should().Throw<RepositoryCompilationException>().WithMessage("RGV1004:*");
        }
        else
        {
            report.ThrowIfFailed();
        }
    }

    [Fact]
    public void ReportsAreDeterministicAndCollectionsAreFrozen()
    {
        using var context = Context();
        var references = References();
        var sources = new[] { new RepositoryCompilationSource("B.cs", "public class B { public MissingB Value; }"), new("A.cs", "public class A { public MissingA Value; }") };
        var options = new RepositoryCompilationOptions(references, additionalSources: sources);
        var first = Bundle(context).ValidateCompilation(options);
        var reversed = Bundle(context).ValidateCompilation(new(references.Reverse(), additionalSources: sources.Reverse()));
        first.Diagnostics.Should().Equal(reversed.Diagnostics);
        ((IList<RepositoryCompilationDiagnostic>)first.Diagnostics).IsReadOnly.Should().BeTrue();
        sources[0] = new("Replaced.cs", "");
        references[0] = "does-not-exist.dll";
        options.References[0].Should().NotBe("does-not-exist.dll");
        options.AdditionalSources[0].Name.Should().Be("B.cs");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("notAssembly")]
    [InlineData("blank")]
    public void InvalidReferencesAreStructuredErrorsWithoutHostFrameworkFallback(string scenario)
    {
        using var context = Context();
        var paths = scenario switch
        {
            "empty" => Array.Empty<string>(),
            "missing" => [Path.Combine(Path.GetTempPath(), "repogen-reference-" + Guid.NewGuid().ToString("N"), "missing.dll")],
            "notAssembly" => [typeof(CompilationValidationTests).Assembly.Location.Replace(".dll", ".deps.json", StringComparison.Ordinal)],
            _ => [" "]
        };
        var report = Bundle(context).ValidateCompilation(new(paths));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("RGV1001");
        if (scenario == "missing")
        {
            Directory.Exists(Path.GetDirectoryName(paths[0])).Should().BeFalse();
        }
    }

    [Fact]
    public void ContractReferenceFailuresAreDistinctFromImplementationFailures()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), []));
        report.Succeeded.Should().BeFalse();
        report.ContractsChecked.Should().BeFalse();
        report.Diagnostics.Should().ContainSingle().Which.Stage.Should().Be(RepositoryCompilationStage.Contracts);
    }

    [Fact]
    public void CompilerDoesNotReplaceMissingTargetAssembliesWithHostReferences()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new([typeof(CompilationValidationTests).Assembly.Location]));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS0518");
    }

    [Fact]
    public void DuplicateSameFileReferencesAreNormalizedButDistinctFilesWithOneAssemblyNameAreRejected()
    {
        using var context = Context();
        var bundle = Bundle(context);
        bundle.ValidateCompilation(new(References().Concat(References()))).Succeeded.Should().BeTrue();
        var directory = Path.Combine(Path.GetTempPath(), "repogen-ambiguous-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var copy = Path.Combine(directory, "SecondCopy.dll");
            File.Copy(typeof(CompilationValidationTests).Assembly.Location, copy);
            var report = bundle.ValidateCompilation(new(References().Append(copy)));
            report.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("RGV1002");
            report.Succeeded.Should().BeFalse();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("language")]
    [InlineData("duplicateSource")]
    [InlineData("blankSource")]
    [InlineData("nullSource")]
    public void InvalidCompilerInputsHaveStableDiagnostics(string scenario)
    {
        using var context = Context();
        var sources = scenario switch
        {
            "duplicateSource" => new[] { new RepositoryCompilationSource("Same.cs", ""), new("Same.cs", "") },
            "blankSource" => [new RepositoryCompilationSource(" ", "")],
            "nullSource" => [null!],
            _ => Array.Empty<RepositoryCompilationSource>()
        };
        var report = Bundle(context).ValidateCompilation(new(References(), additionalSources: sources, languageVersion: scenario == "language" ? "invalid-version" : "latest"));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("RGV1003");
    }

    [Fact]
    public void ExplicitLanguageVersionChecksGeneratedFeaturesAgainstTheTargetCompilerPolicy()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), languageVersion: "7.3"));
        report.Succeeded.Should().BeFalse();
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void AlreadyCanceledValidationThrowsWithoutCreatingAnyOutput()
    {
        using var context = Context();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action validate = () => Bundle(context).ValidateCompilation(new(References()), cancellation.Token);
        validate.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void SdkAssemblyUnificationWarningPolicyCanBeDisabledExplicitly()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), warningsAsErrors: true, suppressedDiagnosticIds: []));
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS1701" && diagnostic.Severity == RepositoryCompilationSeverity.Warning);
        report.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ExplicitNoWarnPolicyCanSuppressAWarningButCannotHideCompilerErrors()
    {
        using var context = Context();
        var warning = new RepositoryCompilationSource("Warning.cs", "#nullable enable\npublic class WarningDto { public string Value { get; } = null; }");
        Bundle(context).ValidateCompilation(new(References(), additionalSources: [warning], warningsAsErrors: true,
            suppressedDiagnosticIds: ["CS1701", "CS1702", "CS8625"])).Succeeded.Should().BeTrue();
        var error = new RepositoryCompilationSource("Error.cs", "public class ErrorDto { public int Value { get; } = MissingValue; }");
        var report = Bundle(context).ValidateCompilation(new(References(), additionalSources: [error], suppressedDiagnosticIds: ["CS0103"]));
        report.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS0103" && diagnostic.Severity == RepositoryCompilationSeverity.Error);
        report.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void NonCompilerDiagnosticSuppressionIsRejected()
    {
        using var context = Context();
        var report = Bundle(context).ValidateCompilation(new(References(), suppressedDiagnosticIds: ["RGV1001"]));
        report.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("RGV1003");
        report.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void OptionalValidationDoesNotLoadOrRunGeneratedOrConsumerCode()
    {
        using var context = Context();
        var before = AppDomain.CurrentDomain.GetAssemblies().Select(static assembly => assembly.FullName).ToArray();
        var report = Bundle(context).ValidateCompilation(new(References(), additionalSources: [new("Throwing.cs", "public static class Throwing { static Throwing() => throw new System.Exception(\"Never execute\"); }")]));
        report.Succeeded.Should().BeTrue();
        AppDomain.CurrentDomain.GetAssemblies().Select(static assembly => assembly.FullName).Should().NotContain(name => name!.StartsWith("RepoGenValidation_", StringComparison.Ordinal));
        before.Should().NotContain(name => name!.StartsWith("RepoGenValidation_", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeFirstLibraryHasNoCliOrCompilerDependencyAndOldToolTypesForward()
    {
        typeof(RepositoryGenerator).Assembly.GetName().Name.Should().Be("RonSijm.RepoGen.Generation");
        typeof(RepositoryGenerator).Assembly.GetReferencedAssemblies().Select(static name => name.Name)
            .Should().NotContain("RonSijm.RepoGen.Tool").And.NotContain("Microsoft.CodeAnalysis.CSharp");
        var tool = typeof(GeneratorOptions).Assembly;
        tool.GetForwardedTypes().Should().Contain(typeof(RepositoryGenerator)).And.Contain(typeof(RepositoryApiSnapshot)).And.Contain(typeof(RepositoryGenerationBatch));
        tool.GetTypes().Should().NotContain(typeof(RepositoryGenerator));
        tool.GetReferencedAssemblies().Select(static name => name.Name).Should().NotContain("RonSijm.RepoGen.Validation").And.NotContain("Microsoft.CodeAnalysis.CSharp");
        Type.GetType(typeof(RepositoryGenerator).FullName + ", RonSijm.RepoGen.Tool", throwOnError: true).Should().Be(typeof(RepositoryGenerator));
    }

    [Fact]
    public void MultiContextBatchCompilesTogetherAndValidationNeverWritesDestinations()
    {
        using var primary = new MultiPrimaryContext(new DbContextOptionsBuilder<MultiPrimaryContext>().UseSqlite("Data Source=:memory:").Options);
        using var replica = new MultiReplicaContext(new DbContextOptionsBuilder<MultiReplicaContext>().UseSqlite("Data Source=:memory:").Options);
        var directory = Path.Combine(Path.GetTempPath(), "repogen-compile-batch-" + Guid.NewGuid().ToString("N"));
        var batch = RepositoryGenerator.GenerateMany(new(primary, Output("primary"), MultiDesign("Compile.Primary")), new(replica, Output("replica"), MultiDesign("Compile.Replica")));
        var report = batch.ValidateCompilation(new(References(), warningsAsErrors: true));
        report.Diagnostics.Should().BeEmpty();
        report.Succeeded.Should().BeTrue();
        Directory.Exists(directory).Should().BeFalse();

        RepositoryOutputLocations Output(string name) => new(Path.Combine(directory, name, "Repositories.g.cs"), Projections: Path.Combine(directory, name, "Queries.g.cs"), Manifest: Path.Combine(directory, name, "manifest.json"));

        static RepositoryDesign MultiDesign(string name)
        {
            var builder = new RepositoryModelBuilder();
            builder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().UseGeneratedNamespace(name));
            return builder.Build();
        }
    }

    internal static string[] References() => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "RepoGen.CompilationReferences.txt"))
        .Append(typeof(CompilationValidationTests).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] ContractReferences() => References().Where(path =>
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" || name.StartsWith("System.", StringComparison.Ordinal) ||
               path == typeof(CompilationValidationTests).Assembly.Location || name == "RonSijm.RepoGen.Abstractions";
    }).ToArray();

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryGenerationBundle Bundle(ClosedContext context, string mode = "closed", RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseGeneratedNamespace("Compiled.Persistence");
            if (mode == "closed")
            {
                options.UseClosedRepositories("Compiled.Contracts");
            }

            if (mode == "cqrs")
            {
                options.UseCqrsSelectors();
            }
        });
        var entity = builder.Entity<ClosedRow>();
        if (mode != "compatibility")
        {
            entity.WriteEnabled = false;
        }

        entity.HasProjection(row => new ClosedRowDto(row.Id, row.Name));
        entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<ClosedRowDto>().List();
        return RepositoryGenerator.Generate(context, builder.Build(), layout).WithApiSnapshotIfClosed(mode);
    }
}

internal static class CompilationValidationTestHelpers
{
    internal static RepositoryGenerationBundle WithApiSnapshotIfClosed(this RepositoryGenerationBundle bundle, string mode)
        => mode == "closed" ? bundle.WithApiSnapshot() : bundle;
}