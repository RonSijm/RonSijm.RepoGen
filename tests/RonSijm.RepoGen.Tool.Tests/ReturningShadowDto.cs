namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ReturningShadowDto(int TenantId, int Id, string Text, int Quantity, int Revision, string Normalized,
    int ComputedVersion, int Credit, ReturningState? Status, string? OptionalNote, byte[]? Fingerprint, string Echo);
