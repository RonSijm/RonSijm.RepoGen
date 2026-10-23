namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCollections;

public sealed record ChecklistChange(int Id, int OwnerId, IReadOnlyList<ChecklistItemInput> Items, int ExpectedVersion = 1);
