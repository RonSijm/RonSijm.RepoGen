namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public sealed record VersionedChecklistSeed(int Id, string Title, int Counter, int Revision);
