namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;

public sealed record VersionedItemChange(int Id, string Code, string Text, int ExpectedRevision);
