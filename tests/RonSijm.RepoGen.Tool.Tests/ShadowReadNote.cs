namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadNote
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Hidden { get; set; }
    public int? FolderId { get; set; }
    public ShadowReadFolder? Folder { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Tests a collision with a generated shadow slot name.")]
    public int __repoGenProperty0 { get; set; }
}
