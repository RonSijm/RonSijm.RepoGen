namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;

public sealed record TenantNoteSeed(int Id, string TenantId, string Title, bool Deleted = false);
