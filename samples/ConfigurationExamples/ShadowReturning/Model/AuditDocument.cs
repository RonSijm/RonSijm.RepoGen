namespace RonSijm.RepoGen.ConfigurationExamples.Models.ShadowReturning;

public sealed class AuditDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Revision { get; set; } = 1;
}
