namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatchResults;

public sealed record NotebookReceipt(int TenantId, int Id, string Code, string Name, string Label, IReadOnlyList<string> Notes);
