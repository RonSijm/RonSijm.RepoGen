namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLinks;

public sealed record ChangeLabels(int BoardId, int ExpectedVersion, IReadOnlyList<LabelInput> Labels);
