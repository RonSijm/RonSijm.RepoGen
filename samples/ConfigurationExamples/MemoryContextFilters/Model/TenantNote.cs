namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryContextFilters;

public sealed class TenantNote
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Deleted { get; set; }
}
