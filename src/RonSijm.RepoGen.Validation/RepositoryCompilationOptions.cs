namespace RonSijm.RepoGen.Validation;

/// <summary>A handwritten consumer or supporting source to compile with the generated API; never written or executed.</summary>
public sealed record RepositoryCompilationSource(string Name, string Contents);

/// <summary>Explicit target references and compiler policy. No host/runtime assembly discovery is performed.</summary>
public sealed class RepositoryCompilationOptions
{
    public RepositoryCompilationOptions(IEnumerable<string> references, IEnumerable<string>? contractReferences = null,
        IEnumerable<RepositoryCompilationSource>? additionalSources = null, string languageVersion = "latest", bool warningsAsErrors = false,
        IEnumerable<string>? suppressedDiagnosticIds = null, IEnumerable<string>? testDoubleReferences = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageVersion);
        References = Array.AsReadOnly(references.ToArray());
        ContractReferences = contractReferences is null ? null : Array.AsReadOnly(contractReferences.ToArray());
        TestDoubleReferences = testDoubleReferences is null ? null : Array.AsReadOnly(testDoubleReferences.ToArray());
        AdditionalSources = Array.AsReadOnly((additionalSources ?? []).ToArray());
        LanguageVersion = languageVersion;
        WarningsAsErrors = warningsAsErrors;
        // These are also suppressed by the .NET SDK's Microsoft.CSharp.Core.targets.
        // Passing an empty list requests stricter assembly-identity reporting.
        SuppressedDiagnosticIds = Array.AsReadOnly((suppressedDiagnosticIds ?? ["CS1701", "CS1702"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<string> References { get; }

    /// <summary>If supplied, compile generated contracts independently with this EF-free reference set first.</summary>
    public IReadOnlyList<string>? ContractReferences { get; }

    /// <summary>If supplied, compile generated contracts and test doubles independently with these EF-free application/testing references.</summary>
    public IReadOnlyList<string>? TestDoubleReferences { get; }

    public IReadOnlyList<RepositoryCompilationSource> AdditionalSources { get; }

    /// <summary>The target's C# version, not a target-framework selection. Supported versions depend on the packaged compiler.</summary>
    public string LanguageVersion { get; }

    public bool WarningsAsErrors { get; }
    public IReadOnlyList<string> SuppressedDiagnosticIds { get; }
}
