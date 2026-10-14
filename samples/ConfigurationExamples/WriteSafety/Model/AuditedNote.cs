namespace RonSijm.RepoGen.ConfigurationExamples.Models.WriteSafety;

public sealed class AuditedNote
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Title { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public int Version { get; set; } = 1;
}
