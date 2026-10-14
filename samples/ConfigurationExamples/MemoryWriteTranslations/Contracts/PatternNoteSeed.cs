namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;

public sealed record PatternNoteSeed(int Id, string Title, int Version = 1, bool Hidden = false);
