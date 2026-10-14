namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;

internal sealed class NoteLabelLink
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int NoteId { get; set; }
    public int? LabelId { get; set; }
}
