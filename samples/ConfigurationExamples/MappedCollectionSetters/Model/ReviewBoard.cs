namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedCollectionSetters;

internal sealed class ReviewBoard
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<ReviewItem> Items { get; set; } = [];
}
