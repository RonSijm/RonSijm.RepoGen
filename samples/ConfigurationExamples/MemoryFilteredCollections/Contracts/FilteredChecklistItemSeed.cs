namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;

public sealed record FilteredChecklistItemSeed(int Id, int ChecklistId, string Code, string Text, int Version = 1, bool Archived = false, bool Hidden = false);
