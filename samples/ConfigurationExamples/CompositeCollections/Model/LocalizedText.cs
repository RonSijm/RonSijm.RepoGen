namespace RonSijm.RepoGen.ConfigurationExamples.Models.CompositeCollections;

internal sealed class LocalizedText
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int DocumentId { get; set; }
    public LocalizedDocument? Document { get; set; }
    public string Code { get; set; } = "";
    public string Locale { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}
