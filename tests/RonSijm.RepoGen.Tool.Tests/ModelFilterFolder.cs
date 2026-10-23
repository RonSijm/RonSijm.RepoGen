namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterFolder
{
    public int Id { get; set; }
    public bool Archived { get; set; }
    public List<ModelFilterRow> Rows { get; set; } = [];
}
