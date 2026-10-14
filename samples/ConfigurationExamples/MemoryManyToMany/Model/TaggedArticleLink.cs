namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;

internal sealed class TaggedArticleLink
{
    public int TenantId { get; set; }
    public int ArticleId { get; set; }
    public int TagId { get; set; }
}
