namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ProxyReadTotal(string Category, int Count, int Score)
{
    public ProxyReadTotal() : this("", 0, 0) { }
}
