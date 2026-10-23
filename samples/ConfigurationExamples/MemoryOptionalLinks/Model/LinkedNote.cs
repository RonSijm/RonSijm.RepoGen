namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;

internal sealed class LinkedNote
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public IReadOnlyCollection<NoteLabel> Labels { get; } = new List<NoteLabel>();
}
