namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;

public sealed record PatternNoteRename(int Id, string Pattern, string Title, int ExpectedVersion = 1);
