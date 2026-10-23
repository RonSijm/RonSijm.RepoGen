namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelFilters;

internal sealed class StoredFilteredNote
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
    public int GroupId { get; set; }
}
