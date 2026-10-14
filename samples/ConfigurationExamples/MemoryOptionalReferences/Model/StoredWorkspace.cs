namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;

internal sealed class StoredWorkspace
{
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<StoredAssignment> Assignments { get; set; } = new List<StoredAssignment>();
}

