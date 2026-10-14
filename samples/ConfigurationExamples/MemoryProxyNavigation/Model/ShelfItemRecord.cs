namespace RonSijm.RepoGen.ConfigurationExamples.Models.ProxyShelves;

internal sealed class ShelfItemRecord
{
    public int Id { get; set; }
    public string? ShelfCode { get; set; }
    public string? ShelfRegion { get; set; }
    public int? CategoryId { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public bool Hidden { get; set; }
    public ShelfRecord? Shelf { get; private set; }
    public ShelfCategoryRecord? Category { get; private set; }
}
