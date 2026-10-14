namespace RonSijm.RepoGen.Validation;

public enum RepositoryCompilationStage
{
    References,
    Contracts,
    Implementation,
    TestDoubles
}

public enum RepositoryCompilationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A compiler or validation diagnostic with stable stage/file and one-based source coordinates when available.</summary>
public sealed record RepositoryCompilationDiagnostic(
    string Id,
    RepositoryCompilationSeverity Severity,
    RepositoryCompilationStage Stage,
    string Message,
    string? File = null,
    int? Line = null,
    int? Column = null);

public sealed class RepositoryCompilationReport
{
    internal RepositoryCompilationReport(IEnumerable<RepositoryCompilationDiagnostic> diagnostics, bool warningsAsErrors, bool contractsChecked, bool testDoublesChecked = false)
    {
        Diagnostics = Array.AsReadOnly(diagnostics.OrderBy(static diagnostic => diagnostic.Stage)
            .ThenBy(static diagnostic => diagnostic.File, StringComparer.Ordinal).ThenBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column).ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray());
        WarningsAsErrors = warningsAsErrors;
        ContractsChecked = contractsChecked;
        TestDoublesChecked = testDoublesChecked;
    }

    public IReadOnlyList<RepositoryCompilationDiagnostic> Diagnostics { get; }
    public bool WarningsAsErrors { get; }

    /// <summary>True only when a separate contract compilation was attempted with the explicitly provided contract references.</summary>
    public bool ContractsChecked { get; }

    /// <summary>True only when separate test-double compilation was attempted with explicit test-double references.</summary>
    public bool TestDoublesChecked { get; }

    public bool Succeeded => !Diagnostics.Any(diagnostic => diagnostic.Severity == RepositoryCompilationSeverity.Error ||
                                                            WarningsAsErrors && diagnostic.Severity == RepositoryCompilationSeverity.Warning);

    public void ThrowIfFailed()
    {
        if (!Succeeded)
        {
            throw new RepositoryCompilationException(this);
        }
    }
}

public sealed class RepositoryCompilationException : InvalidOperationException
{
    internal RepositoryCompilationException(RepositoryCompilationReport report)
        : base("RGV1004: Generated C# compilation failed. " + string.Join("; ", report.Diagnostics
            .Where(diagnostic => diagnostic.Severity == RepositoryCompilationSeverity.Error || report.WarningsAsErrors && diagnostic.Severity == RepositoryCompilationSeverity.Warning)
            .Take(5).Select(static diagnostic => diagnostic.Id + ": " + diagnostic.Message))) => Report = report;

    public RepositoryCompilationReport Report { get; }
}
