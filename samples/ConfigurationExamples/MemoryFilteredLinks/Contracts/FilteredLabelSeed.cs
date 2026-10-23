namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;

public sealed record FilteredLabelSeed(int Id, string Name, bool Archived = false, bool Hidden = false);
