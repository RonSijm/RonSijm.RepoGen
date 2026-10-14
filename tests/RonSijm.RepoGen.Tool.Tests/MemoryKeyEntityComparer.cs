namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyEntityComparer(MemoryKeyRow row) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y) && row.Code.Length == 0;
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
}
