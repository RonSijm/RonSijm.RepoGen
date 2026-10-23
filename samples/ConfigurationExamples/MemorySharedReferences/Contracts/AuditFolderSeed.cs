namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public sealed record AuditFolderSeed(int PartitionId, int Id, int Version = 1);
