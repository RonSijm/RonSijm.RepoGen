namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterFieldKey(string tenant)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Intentional public-field key for the binding regression test.")]
    public readonly string Tenant = tenant;
}
