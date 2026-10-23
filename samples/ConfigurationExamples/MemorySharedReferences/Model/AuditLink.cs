namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedReferences;

internal sealed class AuditLink
{
    public int PartitionId { get; set; }
    public int FolderId { get; set; }
    public AuditFolder? Folder { get; set; }
    public int ResourceId { get; set; }
    public AuditResource? Resource { get; set; }
    public int ActorId { get; set; }
    public AuditActor? Actor { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
}
