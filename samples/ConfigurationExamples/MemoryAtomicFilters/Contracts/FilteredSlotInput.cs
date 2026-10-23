namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;

public sealed record FilteredSlotInput(int Id, string Scope, string Code, string Value,
    int ExpectedVersion = 0, bool Deleted = false, bool Hidden = false);
