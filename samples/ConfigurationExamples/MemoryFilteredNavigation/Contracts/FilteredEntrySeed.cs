namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredNavigation;

public sealed record FilteredEntrySeed(int Id, int? ShelfId, string Title, bool Deleted = false, bool Hidden = false);
