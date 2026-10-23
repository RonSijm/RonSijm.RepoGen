namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public sealed record VersionedItemSeed(int Id, int ChecklistId, string Code, string Text, int Counter, int Revision);
