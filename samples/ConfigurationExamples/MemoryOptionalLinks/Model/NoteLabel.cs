namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;

internal sealed class NoteLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public IReadOnlyCollection<LinkedNote> Notes { get; } = new List<LinkedNote>();
}
