namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;

public sealed record AuditedNoteView(int Id, int OwnerId, string Title, string CreatedBy, int Version);
