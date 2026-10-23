namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;

public sealed record ReviewEntriesRequest(IReadOnlyList<ReviewEntryEdit> Entries);
