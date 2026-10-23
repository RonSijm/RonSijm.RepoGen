namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;

public sealed record FilteredLinkSeed(int Id, int TenantId, int ArticleId, int LabelId, bool Archived = false);
