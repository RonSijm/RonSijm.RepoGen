namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;

public sealed record FilteredSlotSeed(int Id, string Scope, string Code, string Value,
    int Version = 0, bool Deleted = false, bool Hidden = false);
