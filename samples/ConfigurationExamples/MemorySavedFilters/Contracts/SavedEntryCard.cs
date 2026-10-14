namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public sealed record SavedEntryCard(int Id, string Title, int? VisibleShelfId, int RelatedCount);
