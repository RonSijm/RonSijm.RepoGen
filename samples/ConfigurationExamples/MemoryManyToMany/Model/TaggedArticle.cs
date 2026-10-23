namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;

internal sealed class TaggedArticle
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public IReadOnlyCollection<ArticleTag> Tags { get; } = new List<ArticleTag>();
}
