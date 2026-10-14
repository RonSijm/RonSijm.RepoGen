namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationBoundaryOwner
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public NavigationBoundaryTarget? ReadOnly { get; }
    public NavigationBoundaryTarget? InitOnly { get; init; }
    public NavigationBoundaryTarget? ShadowReference { get; set; }
    public NavigationBoundaryTarget? ConvertedReference { get; set; }
    public HashSet<NavigationBoundaryTarget> CustomCollection { get; set; } = [];
    public List<NavigationBoundaryTarget> Tags { get; set; } = [];
}
