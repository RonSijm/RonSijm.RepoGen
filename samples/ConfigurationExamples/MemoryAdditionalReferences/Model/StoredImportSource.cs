namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

internal sealed class StoredImportSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<StoredMembership> Memberships { get; set; } = [];
}
