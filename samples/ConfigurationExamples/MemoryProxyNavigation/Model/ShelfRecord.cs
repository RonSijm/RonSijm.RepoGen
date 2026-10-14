namespace RonSijm.RepoGen.ConfigurationExamples.Models.ProxyShelves;

internal sealed class ShelfRecord
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Region { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    // EF can load this collection, but the EF-free fixture never constructs this entity or mutates it.
    public List<ShelfItemRecord> Items { get; } = [];
}
