namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public sealed record AuditFolderView(int PartitionId, int Id, int Version, List<AuditReferenceView> Links);
