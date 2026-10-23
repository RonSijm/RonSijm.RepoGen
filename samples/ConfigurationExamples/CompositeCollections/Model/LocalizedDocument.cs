namespace RonSijm.RepoGen.ConfigurationExamples.Models.CompositeCollections;

internal sealed class LocalizedDocument
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<LocalizedText> Texts { get; set; } = new List<LocalizedText>();
}
