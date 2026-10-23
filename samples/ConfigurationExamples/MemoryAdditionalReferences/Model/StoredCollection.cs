namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

internal sealed class StoredCollection
{
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public List<StoredMembership> Memberships { get; set; } = [];
}
