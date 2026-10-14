namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyCountingComparer : IEqualityComparer<string>
{
    private readonly StringComparer comparer;
    private static int created;
    public static int Created => Volatile.Read(ref created);
    public MemoryKeyCountingComparer(StringComparer comparer)
    {
        this.comparer = comparer;
        Interlocked.Increment(ref created);
    }
    public bool Equals(string? x, string? y) => comparer.Equals(x, y);
    public int GetHashCode(string obj) => comparer.GetHashCode(obj);
}
