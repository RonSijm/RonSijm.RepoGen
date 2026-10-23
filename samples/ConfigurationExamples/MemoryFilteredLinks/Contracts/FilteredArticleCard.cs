namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;

public sealed record FilteredArticleCard(int TenantId, int Id, string Title, List<string> Labels);
