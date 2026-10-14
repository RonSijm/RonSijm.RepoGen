namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLoadingPolicies;

public sealed record CatalogGroupDetail(int Id, string Name, List<CatalogEntryCard> Entries);
