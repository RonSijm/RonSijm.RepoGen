namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;

internal sealed class StoredOrigin
{
    public int Id { get; set; }
    public int Partition { get; set; }
    public string Code { get; set; } = "";
    public ICollection<StoredAssignment> Assignments { get; set; } = new List<StoredAssignment>();
}

