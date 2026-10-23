namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryRelationshipComparers;

internal sealed class StoredFolderEntry
{
    public int Id { get; set; }
    public int Region { get; set; }
    public string? FolderCode { get; set; }
    public string Caption { get; set; } = "";
    public StoredFolder? Folder { get; }
}
