namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.BatchWrites;

public sealed record EditNotesRequest(int OwnerId, IReadOnlyList<NoteEdit> Items);
