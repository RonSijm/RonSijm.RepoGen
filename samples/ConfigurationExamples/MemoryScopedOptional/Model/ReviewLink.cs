namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScopedOptional;

internal sealed class ReviewLink
{
    public int PartitionId { get; set; }
    public int FolderId { get; set; }
    public ReviewFolder? Folder { get; set; }
    public int ResourceId { get; set; }
    public ReviewResource? Resource { get; set; }
    public string? ReviewerRegion { get; set; }
    public string? ReviewerCode { get; set; }
    public ReviewPerson? Reviewer { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
}
