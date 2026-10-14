namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

internal sealed class StoredCatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<StoredMembership> Memberships { get; set; } = [];
}
