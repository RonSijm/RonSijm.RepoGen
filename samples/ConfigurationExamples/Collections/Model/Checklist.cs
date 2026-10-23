namespace RonSijm.RepoGen.ConfigurationExamples.Models.Collections;

public sealed class Checklist
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<ChecklistItem> Items { get; set; } = new List<ChecklistItem>();
}
