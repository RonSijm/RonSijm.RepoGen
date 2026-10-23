namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;

internal sealed class StoredFolderResource
{
    public int PartitionId { get; set; }
    public int FolderId { get; set; }
    public int ResourceId { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
    public StoredFolder? Folder { get; set; }
    public StoredResource? Resource { get; set; }
}
