namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceTarget
{
    public int Id { get; set; }
    public ICollection<OptionalReferenceRow> Links { get; set; } = new List<OptionalReferenceRow>();
}

