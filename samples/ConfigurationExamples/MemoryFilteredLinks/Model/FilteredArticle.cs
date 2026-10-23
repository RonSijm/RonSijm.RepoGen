namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredLinks;

internal sealed class FilteredArticle
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Archived { get; set; }
    public IReadOnlyCollection<FilteredLabel> Labels { get; } = new List<FilteredLabel>();
}
