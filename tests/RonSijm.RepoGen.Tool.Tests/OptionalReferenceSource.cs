namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceSource
{
    public int Id { get; set; }
    public ICollection<OptionalReferenceRow> Links { get; set; } = new List<OptionalReferenceRow>();
}

