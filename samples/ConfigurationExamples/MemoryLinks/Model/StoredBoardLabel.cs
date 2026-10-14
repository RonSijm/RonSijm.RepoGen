namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;

internal sealed class StoredBoardLabel
{
    public int BoardId { get; set; }
    public int LabelId { get; set; }
    public string Caption { get; set; } = "";
    public int Version { get; set; } = 1;
    public StoredBoard? Board { get; set; }
    public StoredLabel? Label { get; set; }
}
