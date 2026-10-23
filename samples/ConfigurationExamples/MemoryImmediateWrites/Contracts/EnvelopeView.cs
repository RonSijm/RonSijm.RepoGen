namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryImmediateWrites;

public sealed record EnvelopeView(int Id, string Body, int Attempts, int PreviousAttempts, int Version);
