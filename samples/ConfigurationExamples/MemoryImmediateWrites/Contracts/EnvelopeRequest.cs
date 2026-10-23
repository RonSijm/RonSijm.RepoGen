namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryImmediateWrites;

public sealed record EnvelopeRequest(string Queue, int Id = 0, int ExpectedVersion = 1, string Body = "");
