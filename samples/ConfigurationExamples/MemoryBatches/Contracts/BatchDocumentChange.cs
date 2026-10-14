using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;

public sealed record BatchDocumentChange(string Code, Optional<string?> Title, int ExpectedVersion = 1);
