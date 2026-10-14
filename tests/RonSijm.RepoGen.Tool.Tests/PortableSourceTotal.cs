namespace RonSijm.RepoGen.Tool.Tests;

public sealed record PortableSourceTotal(string Customer, int Count, int Total)
{
    public PortableSourceTotal() : this("", 0, 0) { }
}
