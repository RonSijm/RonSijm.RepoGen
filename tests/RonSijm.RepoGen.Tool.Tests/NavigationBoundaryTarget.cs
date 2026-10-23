namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationBoundaryTarget
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int? OwnerId { get; set; }
    public bool Hidden { get; set; }
    public List<NavigationBoundaryOwner> Owners { get; set; } = [];
}
