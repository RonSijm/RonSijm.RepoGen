namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScopedOptional;

internal sealed class ReviewPerson
{
    public int Id { get; set; }
    public int PartitionId { get; set; }
    public string Region { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
