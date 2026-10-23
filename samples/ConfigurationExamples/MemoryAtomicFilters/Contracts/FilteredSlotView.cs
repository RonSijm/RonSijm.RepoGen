namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;

public sealed record FilteredSlotView(int Id, string Code, string Value, int Version, bool Deleted);
