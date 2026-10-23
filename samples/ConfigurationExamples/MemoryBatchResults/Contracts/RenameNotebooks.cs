namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatchResults;

public sealed record RenameNotebooks(string Label, IReadOnlyList<RenameNotebook> Items);
