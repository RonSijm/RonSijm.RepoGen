namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;

public sealed record FilteredChecklistChange(int Id, int OwnerId, IReadOnlyList<FilteredChecklistItemInput> Items, int ExpectedVersion = 1);
