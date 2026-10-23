namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceReviewer
{
    public int Id { get; set; }
    public int Region { get; set; }
    public string Code { get; set; } = "";
    public ICollection<OptionalReferenceRow> Links { get; set; } = new List<OptionalReferenceRow>();
}

