namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ReturningMappedView(int Id, string Title, string Caption, int Audit, ReturningState? State, byte[]? Fingerprint, string? Note);
