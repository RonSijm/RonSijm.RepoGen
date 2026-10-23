namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;

public sealed record FilteredChecklistSeed(int Id, int OwnerId, string Name, int Version = 1, bool Archived = false);
