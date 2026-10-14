namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;

public sealed class CatalogEntry
{
    public int Id { get; set; }
    public string? GroupCode { get; set; }
    public string? GroupRegion { get; set; }
    public int? CategoryId { get; set; }
    public string Title { get; set; } = "";
    public int Quantity { get; set; }
    public bool Hidden { get; set; }
    public CatalogGroup? Group { get; set; }
    public CatalogCategory? Category { get; set; }
}
