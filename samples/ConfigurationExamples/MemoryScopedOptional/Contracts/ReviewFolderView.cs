namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;

public sealed record ReviewFolderView(int PartitionId, int Id, int Version, List<ReviewReferenceView> Links);
