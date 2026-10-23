namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;

public sealed record DraftRequest(int Id, string Title, int ExpectedVersion = 1);
