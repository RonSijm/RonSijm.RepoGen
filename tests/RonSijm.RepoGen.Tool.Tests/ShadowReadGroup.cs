namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ShadowReadGroup(string Code, int Count, int Total)
{
    public ShadowReadGroup() : this("", 0, 0) { }
}
