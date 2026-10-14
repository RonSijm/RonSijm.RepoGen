namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCollections;

public sealed record ChecklistItemSeed(int Id, int ChecklistId, string Code, string Text, int Version = 1, bool Hidden = false, string FixtureNote = "arranged");
