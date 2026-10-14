namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCreateBatches;

public sealed record CreateEntryBatch(IReadOnlyList<NewEntry> Items, string Label = "batch");
