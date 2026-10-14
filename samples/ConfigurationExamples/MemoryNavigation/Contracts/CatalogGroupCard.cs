namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;

public sealed record CatalogGroupCard(int Id, string Name, int Quantity, IReadOnlyList<CatalogEntryCard> Entries);
