namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyDirectory;

// Arrangement data, not a persistence entity or a query result.
public sealed record DirectorySeed(int Key, string Label, int Credits, bool Archived = false);
