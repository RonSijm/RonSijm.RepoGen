namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedResults;

public sealed record SaveNotebook(int Id, string Name, IReadOnlyList<NoteInput> Notes);
