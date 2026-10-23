namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public sealed record AuditLinkSeed(int PartitionId, int FolderId, int ResourceId, int ActorId, string Caption, int Version = 1);
