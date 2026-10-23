namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public sealed record SavedEntrySeed(int Id, int? ShelfId, string Title, bool Deleted = false, bool Hidden = false);
