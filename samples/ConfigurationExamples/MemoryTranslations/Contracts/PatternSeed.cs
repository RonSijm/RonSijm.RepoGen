namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryTranslations;

public sealed record PatternSeed(int Id, string Title, string? Summary, int Price, bool Hidden = false);
