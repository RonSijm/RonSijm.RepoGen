namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public sealed record ChangeAuditReferences(int PartitionId, int FolderId, IReadOnlyList<AuditReferenceInput> Items, int ExpectedVersion = 1);
