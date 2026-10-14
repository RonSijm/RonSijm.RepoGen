namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;

public sealed record FolderView(int PartitionId, int Id, int Version, IReadOnlyList<FolderResourceView> Resources);
