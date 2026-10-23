namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

internal sealed class StoredMembership
{
    public int CollectionId { get; set; }
    public int ItemId { get; set; }
    public string ContributorRegion { get; set; } = "";
    public string ContributorCode { get; set; } = "";
    public int SourceId { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
    public StoredCollection? Collection { get; set; }
    public StoredCatalogItem? Item { get; set; }
    public StoredContributor? Contributor { get; set; }
    public StoredImportSource? Source { get; set; }
}
