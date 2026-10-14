namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNullTargets;

public sealed record NotebookSummary(int Id, string Title, int LinkCount, int MissingCount, IReadOnlyList<string?> Labels);

