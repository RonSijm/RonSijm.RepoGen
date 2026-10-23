namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record AuditDocumentChange(int Id, string Title, int Revision, string NormalizedTitle, int TitleLength, string? SourceTag);
