namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record CapturedNoteChange(int Id, string Title, string NormalizedTitle, int Version, string? SourceTag);
