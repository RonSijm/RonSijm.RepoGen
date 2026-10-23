namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;

public sealed record RenameNoteRequest(int Id, int OwnerId, string Title, int ExpectedVersion);
