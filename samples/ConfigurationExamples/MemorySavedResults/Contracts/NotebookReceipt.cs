namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedResults;

public sealed record NotebookReceipt(int Id, string Name, IReadOnlyList<NoteReceipt> Notes);
