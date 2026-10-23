namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;

public sealed record ChangeFolderResources(int PartitionId, int FolderId, int ExpectedVersion, IReadOnlyList<ResourceReference> Resources);
