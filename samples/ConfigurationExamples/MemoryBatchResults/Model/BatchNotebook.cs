namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatchResults;

internal sealed class BatchNotebook
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ICollection<BatchNote> Notes { get; set; } = new List<BatchNote>();
}
