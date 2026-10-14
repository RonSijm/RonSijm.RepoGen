namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatchResults;

internal sealed class BatchNote
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int NotebookId { get; set; }
    public string Text { get; set; } = "";
    public bool Archived { get; set; }
    public BatchNotebook? Notebook { get; set; }
}
