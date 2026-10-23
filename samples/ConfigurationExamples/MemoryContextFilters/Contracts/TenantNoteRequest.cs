namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;

public sealed record TenantNoteRequest(TenantPartition Partition, int Id = 0, string Title = "");
