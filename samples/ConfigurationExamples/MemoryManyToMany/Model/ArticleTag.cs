namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;

internal sealed class ArticleTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public IReadOnlyCollection<TaggedArticle> Articles { get; } = new List<TaggedArticle>();
}
