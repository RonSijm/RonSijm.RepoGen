namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicCapture;

public sealed record ReservationChange(int Id, string Code, int Delta = 1, int ExpectedRevision = 0);
