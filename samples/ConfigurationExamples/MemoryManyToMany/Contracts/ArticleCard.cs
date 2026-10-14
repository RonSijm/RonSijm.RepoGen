namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryManyToMany;

public sealed record ArticleCard(int TenantId, int Id, string Title, List<string> Tags);
