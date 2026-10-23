namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;

public sealed record AuditReferenceInput(int PartitionId, int ResourceId, int ActorId, string Caption, int ExpectedVersion = 1);
