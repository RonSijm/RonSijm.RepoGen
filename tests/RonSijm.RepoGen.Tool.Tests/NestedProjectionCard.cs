namespace RonSijm.RepoGen.Tool.Tests;

public sealed record NestedProjectionCard(int Id, NestedProjectionDetail? Detail,
    IReadOnlyList<NestedProjectionDetail> Metrics, IReadOnlyList<string> Labels);
