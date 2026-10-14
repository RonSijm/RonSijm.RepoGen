using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Tool;

namespace RonSijm.RepoGen.Validation;

/// <summary>Opt-in compiler validation. Emits to memory only, never loads generated code, and never writes a bundle.</summary>
public static class RepositoryCompilationValidator
{
    public static RepositoryCompilationReport ValidateCompilation(this RepositoryGenerationBundle bundle,
        RepositoryCompilationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return Validate([bundle], options, cancellationToken);
    }

    /// <summary>Compile a whole multi-context batch together to check actual cross-context source references.</summary>
    public static RepositoryCompilationReport ValidateCompilation(this RepositoryGenerationBatch batch,
        RepositoryCompilationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return Validate(batch.Bundles, options, cancellationToken);
    }

    private static RepositoryCompilationReport Validate(IReadOnlyList<RepositoryGenerationBundle> bundles,
        RepositoryCompilationOptions options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        token.ThrowIfCancellationRequested();
        var diagnostics = new List<RepositoryCompilationDiagnostic>();
        var testDoublesChecked = false;
        if (!LanguageVersionFacts.TryParse(options.LanguageVersion, out var version))
        {
            diagnostics.Add(Error("RGV1003", RepositoryCompilationStage.References, "Unsupported C# language version: " + options.LanguageVersion));
            return Report(false);
        }

        var parse = new CSharpParseOptions(version);
        if (options.SuppressedDiagnosticIds.Any(static id => id is null || id.Length != 6 || !id.StartsWith("CS", StringComparison.Ordinal) || id.Skip(2).Any(static character => character is < '0' or > '9')))
        {
            diagnostics.Add(Error("RGV1003", RepositoryCompilationStage.References, "Suppressed diagnostics must use full compiler IDs such as CS1701; reference/configuration failures cannot be suppressed."));
            return Report(false);
        }

        var files = bundles.SelectMany(bundle => bundle.Files
            .Where(static file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories or RepositoryOutputKind.Projections or RepositoryOutputKind.TestDoubles)
            .Select(file => (file.Kind, Source: new RepositoryCompilationSource(bundle.Context + "/" + file.Name, file.Contents)))).ToArray();
        var sources = files.Select(static file => file.Source).Concat(options.AdditionalSources).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Name) || source.Contents is null || !names.Add(source.Name))
            {
                diagnostics.Add(Error("RGV1003", RepositoryCompilationStage.References, "Sources must have distinct nonempty names and non-null contents."));
            }
        }

        if (diagnostics.Count > 0)
        {
            return Report(false);
        }

        var references = References(options.References, RepositoryCompilationStage.Implementation);
        var contracts = files.Where(static file => file.Kind == RepositoryOutputKind.Contracts).Select(static file => file.Source).ToArray();
        var contractsChecked = false;
        if (options.ContractReferences is not null && contracts.Length > 0)
        {
            var contractReferences = References(options.ContractReferences, RepositoryCompilationStage.Contracts);
            if (contractReferences is not null)
            {
                Compile(contracts, contractReferences, RepositoryCompilationStage.Contracts);
                contractsChecked = true;
            }
        }

        if (references is not null)
        {
            Compile(sources, references, RepositoryCompilationStage.Implementation);
        }

        var testDoubles = files.Where(static file => file.Kind == RepositoryOutputKind.TestDoubles).Select(static file => file.Source).ToArray();
        if (options.TestDoubleReferences is not null && testDoubles.Length > 0)
        {
            var testingReferences = References(options.TestDoubleReferences, RepositoryCompilationStage.TestDoubles);
            if (testingReferences is not null)
            {
                Compile(contracts.Concat(testDoubles), testingReferences, RepositoryCompilationStage.TestDoubles);
                testDoublesChecked = true;
            }
        }

        return Report(contractsChecked);

        RepositoryCompilationReport Report(bool checkedContracts) => new(diagnostics, options.WarningsAsErrors, checkedContracts, testDoublesChecked);

        MetadataReference[]? References(IReadOnlyList<string> paths, RepositoryCompilationStage stage)
        {
            var result = new List<MetadataReference>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var valid = true;
            if (paths.Count == 0)
            {
                diagnostics.Add(Error("RGV1001", stage, "Explicit target assembly references are required; the host's framework is not selected automatically."));
                return null;
            }

            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(path);
                    var fullPath = Path.GetFullPath(path);
                    if (!seenPaths.Add(fullPath))
                    {
                        continue;
                    }

                    var identity = AssemblyName.GetAssemblyName(fullPath).Name!;
                    if (assemblies.TryGetValue(identity, out var prior))
                    {
                        diagnostics.Add(Error("RGV1002", stage, $"Ambiguous reference '{identity}': '{prior}' and '{fullPath}'. Choose one target assembly.", fullPath));
                        valid = false;
                        continue;
                    }

                    assemblies.Add(identity, fullPath);
                    result.Add(MetadataReference.CreateFromFile(fullPath));
                }
                catch (Exception exception) when (exception is IOException or BadImageFormatException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
                {
                    diagnostics.Add(Error("RGV1001", stage, "Invalid assembly reference: " + exception.Message, path));
                    valid = false;
                }
            }

            return valid ? result.ToArray() : null;
        }

        void Compile(IEnumerable<RepositoryCompilationSource> inputs, MetadataReference[] references, RepositoryCompilationStage stage)
        {
            var trees = inputs.Select(source => CSharpSyntaxTree.ParseText(source.Contents, parse, source.Name, cancellationToken: token)).ToArray();
            var compilation = CSharpCompilation.Create("RepoGenValidation_" + stage, trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                    deterministic: true, specificDiagnosticOptions: options.SuppressedDiagnosticIds.ToDictionary(static id => id, static _ => ReportDiagnostic.Suppress, StringComparer.Ordinal)));
            using var assembly = new MemoryStream();
            foreach (var diagnostic in compilation.Emit(assembly, cancellationToken: token).Diagnostics.Where(static diagnostic => !diagnostic.IsSuppressed && diagnostic.Severity != DiagnosticSeverity.Hidden))
            {
                var location = diagnostic.Location.IsInSource ? diagnostic.Location.GetLineSpan() : (FileLinePositionSpan?)null;
                diagnostics.Add(new(diagnostic.Id, diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => RepositoryCompilationSeverity.Error,
                    DiagnosticSeverity.Warning => RepositoryCompilationSeverity.Warning,
                    _ => RepositoryCompilationSeverity.Info
                }, stage, diagnostic.GetMessage(CultureInfo.InvariantCulture), location?.Path,
                    location?.StartLinePosition.Line + 1, location?.StartLinePosition.Character + 1));
            }
        }
    }

    private static RepositoryCompilationDiagnostic Error(string id, RepositoryCompilationStage stage, string message, string? file = null)
        => new(id, RepositoryCompilationSeverity.Error, stage, message, file);
}
