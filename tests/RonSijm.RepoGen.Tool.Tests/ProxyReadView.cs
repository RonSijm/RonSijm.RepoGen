namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ProxyReadView(int Id, string Name, string Category, int Score, string? Department)
{
    public ProxyReadView() : this(0, "", "", 0, null) { }
}
