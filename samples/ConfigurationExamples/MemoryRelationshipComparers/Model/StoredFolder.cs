namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryRelationshipComparers;

internal sealed class StoredFolder
{
    public int Id { get; set; }
    public int Region { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public IReadOnlyCollection<StoredFolderEntry> Entries { get; } = new List<StoredFolderEntry>();
}
