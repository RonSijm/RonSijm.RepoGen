namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProjectionHelpers;

internal sealed class StoredProjectionMemo
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int Revision { get; set; }
    public bool Hidden { get; set; }
}
