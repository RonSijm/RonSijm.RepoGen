namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public sealed record SavedEntryInput(int Id, string Title, int? ShelfId = null, bool Deleted = false, bool Hidden = false);
