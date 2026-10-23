namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedCollections;

internal sealed class VersionedChecklistItem
{
    public int Id { get; set; }
    public int ChecklistId { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Counter { get; set; }
}
