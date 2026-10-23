namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.WriteSafety;

public sealed record CorrectAttributionRequest(int Id, int OwnerId, string CreatedBy, int ExpectedVersion);
