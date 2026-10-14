using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.BatchWrites;

public sealed record NoteEdit(string Code, Optional<string?> Title, int ExpectedVersion);
