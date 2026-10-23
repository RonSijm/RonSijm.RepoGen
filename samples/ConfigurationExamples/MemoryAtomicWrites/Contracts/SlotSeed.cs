namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicWrites;

public sealed record SlotSeed(int Id, string Scope, string Code, string Value, int Version = 1,
    bool Hidden = false, string FixtureNote = "arranged");
