namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;

public sealed record ResourceReference(int PartitionId, int ResourceId, string Caption, int ExpectedVersion = 1);
