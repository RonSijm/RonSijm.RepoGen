namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelDefaultRow
{
    public int Id { get; set; }
    public int Credits { get; set; } = -1;
    public bool Enabled { get; set; } = true;
    public ModelDefaultLevel Level { get; set; } = ModelDefaultLevel.Unset;
    public string? Label { get; set; }
    public int? Score { get; set; } = -1;
    public byte Priority { get; set; } = byte.MaxValue;
}
