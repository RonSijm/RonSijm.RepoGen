namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCollections;

public sealed record ChecklistItemInput(string Code, string Text, int ExpectedVersion = 1);
