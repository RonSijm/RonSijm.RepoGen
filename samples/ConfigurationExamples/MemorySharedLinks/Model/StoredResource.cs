namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;

internal sealed class StoredResource
{
    public int PartitionId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<StoredFolderResource> Links { get; set; } = [];
}
