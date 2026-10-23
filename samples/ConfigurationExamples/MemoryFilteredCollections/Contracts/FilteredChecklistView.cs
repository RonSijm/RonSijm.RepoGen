namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;

public sealed record FilteredChecklistView(int Id, string Name, int Version, List<string> Items);
