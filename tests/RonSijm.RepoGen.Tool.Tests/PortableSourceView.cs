namespace RonSijm.RepoGen.Tool.Tests;

public sealed record PortableSourceView(int OrderId, string? Customer, int Amount)
{
    public PortableSourceView() : this(0, null, 0) { }
}
