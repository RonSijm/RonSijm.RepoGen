namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredLinks;

internal sealed class FilteredArticleLabel
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ArticleId { get; set; }
    public int LabelId { get; set; }
    public bool Archived { get; set; }
}
