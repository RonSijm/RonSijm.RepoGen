namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;

internal sealed class StoredFolder
{
    public int PartitionId { get; set; }
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public List<StoredFolderResource> Links { get; set; } = [];
}
