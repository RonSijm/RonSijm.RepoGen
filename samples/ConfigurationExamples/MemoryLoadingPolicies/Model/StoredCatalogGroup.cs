namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLoadingPolicies;

internal sealed class StoredCatalogGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<StoredCatalogEntry> Entries { get; set; } = [];
}
