namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.CompositeCollections;

public sealed record TranslationInput(string Code, string Locale, string Text, int ExpectedVersion = 1);
