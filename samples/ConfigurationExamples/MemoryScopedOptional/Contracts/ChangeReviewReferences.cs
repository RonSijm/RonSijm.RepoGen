namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public sealed record ChangeReviewReferences(int PartitionId, int FolderId, IReadOnlyList<ReviewReferenceInput> Items, int ExpectedVersion = 1);
