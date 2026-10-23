namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
    public int? FolderId { get; set; }
    public ModelFilterFolder? Folder { get; set; }
    public string Unmapped => throw new InvalidOperationException("Generation must never read computed entity behavior for " + Id + ".");
}
