namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;

public sealed record FilteredDraftSeed(int Id, string Text, int Version, bool Archived, bool Hidden);
