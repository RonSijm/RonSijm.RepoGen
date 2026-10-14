namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelFilters;

public sealed record FilteredNoteSeed(int Id, string Text, bool Deleted, bool Hidden, int GroupId);
