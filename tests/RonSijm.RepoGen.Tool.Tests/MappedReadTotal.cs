namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedReadTotal(string Tag, int Count, int Total)
{
    public MappedReadTotal() : this("", 0, 0) { }
}
