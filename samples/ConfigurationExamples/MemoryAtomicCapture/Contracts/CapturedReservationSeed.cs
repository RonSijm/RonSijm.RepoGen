namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicCapture;

public sealed record CapturedReservationSeed(int Id, string Code, int Counter, string Label, int Revision, string? Route);
