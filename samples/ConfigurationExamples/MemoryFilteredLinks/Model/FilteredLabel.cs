namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredLinks;

internal sealed class FilteredLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Archived { get; set; }
    public bool Hidden { get; set; }
    public IReadOnlyCollection<FilteredArticle> Articles { get; } = new List<FilteredArticle>();
}
