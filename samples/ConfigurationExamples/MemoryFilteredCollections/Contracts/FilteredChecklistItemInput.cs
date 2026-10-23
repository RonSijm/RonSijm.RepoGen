namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;

public sealed record FilteredChecklistItemInput(string Code, string Text, bool Archived = false, int ExpectedVersion = 1);
