using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Generator.Tests;

internal static class GeneratorTestHarness
{
    public static GeneratorTestResult Run(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = GetReferences();
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RepoGenGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return new GeneratorTestResult(driver.GetRunResult(), outputCompilation);
    }

    private static PortableExecutableReference[] GetReferences()
    {
        _ = typeof(EntityFrameworkQueryableExtensions);
        _ = typeof(GenerateSelectorsAttribute);

        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var assemblyPaths = trustedPlatformAssemblies
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(static assembly => assembly.Location))
            .Append(typeof(GenerateSelectorsAttribute).Assembly.Location)
            .Append(typeof(EntityFrameworkQueryableExtensions).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return assemblyPaths.Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
    }
}

internal sealed record GeneratorTestResult(GeneratorDriverRunResult RunResult, Compilation OutputCompilation)
{
    public string GeneratedSource => string.Join(
        Environment.NewLine,
        RunResult.Results.SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.SourceText.ToString()));

    public IReadOnlyList<Diagnostic> GeneratorDiagnostics => RunResult.Results
        .SelectMany(static result => result.Diagnostics)
        .ToArray();

    public IReadOnlyList<Diagnostic> CompilationErrors => OutputCompilation.GetDiagnostics()
        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        .ToArray();
}