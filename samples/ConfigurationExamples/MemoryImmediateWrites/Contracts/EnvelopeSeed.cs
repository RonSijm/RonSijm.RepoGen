namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryImmediateWrites;

public sealed record EnvelopeSeed(int Id, string Queue, string Body, int Attempts, int PreviousAttempts, int Version, bool Hidden = false);
