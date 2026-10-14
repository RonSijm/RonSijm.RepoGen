namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyCompoundComparer : IEqualityComparer<ValueTuple<string, int>>
{
    public bool Equals((string, int) x, (string, int) y) => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && x.Item2 == y.Item2;
    public int GetHashCode((string, int) obj) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), obj.Item2);
}
