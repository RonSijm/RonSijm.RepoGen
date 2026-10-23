namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SavedBatchView(int TenantId, int Id, string Code, string Name, int Version, string Label, IReadOnlyList<string> Children);
