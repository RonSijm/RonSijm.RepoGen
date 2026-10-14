namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyDerivedInfrastructureComparer(MemoryKeyContext? context) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y) && context is null;
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
}
