namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadLibrary
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Region { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public List<NavigationReadBook> Books { get; set; } = [];
    public NavigationReadProfile? Profile { get; set; }
}
