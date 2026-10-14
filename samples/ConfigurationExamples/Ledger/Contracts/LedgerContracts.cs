// This example's application contracts; compiled by the shared EF-free Contracts project.
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

// Application types have no EF, model, designer or persistence dependency.
public sealed record LedgerRequest(string Database);

public sealed record CreateLedgerEntry(string Database, string Code, string? Text);

public sealed record PatchLedgerEntry(string Database, int Id, Optional<string?> Text, int ExpectedVersion);

public sealed record DeleteLedgerEntry(string Database, int Id);

public sealed record LedgerRow
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Text { get; init; }
    public int Version { get; init; }
    public string NormalizedCode { get; init; } = string.Empty;
}
