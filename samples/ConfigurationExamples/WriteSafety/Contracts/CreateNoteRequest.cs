namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;

public sealed record CreateNoteRequest(int OwnerId, string Title, string CreatedBy);
