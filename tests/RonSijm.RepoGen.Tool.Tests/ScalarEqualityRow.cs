namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityRow
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public string Revision { get; set; } = "V1";
    public int Amount { get; set; }
    public bool Hidden { get; set; }
}
