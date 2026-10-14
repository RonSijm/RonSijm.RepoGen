namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;

internal sealed class StoredSubject
{
    public int Id { get; set; }
    public ICollection<StoredAssignment> Assignments { get; set; } = new List<StoredAssignment>();
}

