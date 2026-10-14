namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = "owner";
    public int Version { get; set; } = 1;
    public ICollection<OptionalReferenceRow> Links { get; set; } = new List<OptionalReferenceRow>();
}

