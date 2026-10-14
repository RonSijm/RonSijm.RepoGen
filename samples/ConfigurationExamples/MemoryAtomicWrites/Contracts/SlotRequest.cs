namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicWrites;

public sealed record SlotRequest(string Scope, string Code = "", string Value = "", int ExpectedVersion = 1, int Id = 0);
