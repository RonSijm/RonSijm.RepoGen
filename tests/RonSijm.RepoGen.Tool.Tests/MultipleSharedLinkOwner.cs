namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultipleSharedLinkOwner
{
    public int Zone { get; set; }
    public int Id { get; set; }
    public int SpaceId { get; set; }
    public List<MultipleSharedLinkRow> Links { get; set; } = [];
}
