namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.CompositeCollections;

public sealed record TranslationSeed(int Id, int TenantId, int DocumentId, string Code, string Locale, string Text, int Version = 1, bool Hidden = false);
