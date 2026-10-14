namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.CompositeCollections;

public sealed record TranslationChange(int TenantId, int Id, IReadOnlyList<TranslationInput> Texts, int ExpectedVersion = 1);
