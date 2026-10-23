namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedReferences;

internal sealed class AuditFolder
{
    public int PartitionId { get; set; }
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<AuditLink> Links { get; set; } = new List<AuditLink>();
}
