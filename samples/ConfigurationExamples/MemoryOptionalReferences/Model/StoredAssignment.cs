namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;

internal sealed class StoredAssignment
{
    public int WorkspaceId { get; set; }
    public int SubjectId { get; set; }
    public int? OriginPartition { get; set; }
    public string? OriginCode { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
    public StoredWorkspace? Workspace { get; set; }
    public StoredSubject? Subject { get; set; }
    public StoredOrigin? Origin { get; set; }
}

