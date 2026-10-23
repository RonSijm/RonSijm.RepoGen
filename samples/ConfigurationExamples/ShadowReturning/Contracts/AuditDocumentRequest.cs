namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record AuditDocumentRequest(int Id, string Title, int ExpectedRevision);
