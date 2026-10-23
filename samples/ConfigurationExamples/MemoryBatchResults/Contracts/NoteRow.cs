namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatchResults;

public sealed record NoteRow(int Id, int TenantId, int NotebookId, string Text, bool Archived);
