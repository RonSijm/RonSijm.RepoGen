namespace RonSijm.RepoGen.ConfigurationExamples.Models.ShadowVersions;

public sealed class VersionedNote
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ChangeCounter { get; set; }
}
