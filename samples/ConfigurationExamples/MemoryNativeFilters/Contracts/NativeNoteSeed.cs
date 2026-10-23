namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeFilters;

public sealed record NativeNoteSeed(int Id, string Title, int Version = 0, bool Deleted = false, bool Hidden = false);
