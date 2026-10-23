namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record NoteVersionBatch(IReadOnlyList<NoteVersionChange> Items);
