namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;

public sealed record FilteredArticleSeed(int TenantId, int Id, string Title, bool Archived = false);
