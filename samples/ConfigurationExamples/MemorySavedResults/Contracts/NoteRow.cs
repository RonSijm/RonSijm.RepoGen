namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedResults;

public sealed record NoteRow(int Id, int NotebookId, string Code, string Text, bool Archived);
