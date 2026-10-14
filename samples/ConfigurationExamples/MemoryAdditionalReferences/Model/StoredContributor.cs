namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

internal sealed class StoredContributor
{
    public int Id { get; set; }
    public string Region { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<StoredMembership> Memberships { get; set; } = [];
}
