namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;

// Explicitly public fixture/entity shapes. No EF attributes, base classes or runtime dependency.
public sealed class CatalogGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Region { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<CatalogEntry> Entries { get; set; } = [];
}
