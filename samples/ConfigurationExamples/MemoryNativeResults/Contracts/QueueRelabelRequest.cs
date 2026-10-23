namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;

public sealed record QueueRelabelRequest(int QueueId, string Prefix, int ExpectedRevision = 1);
