namespace RonSijm.RepoGen.Abstractions;

/// <summary>Non-null component validation for generated composite collection matching keys.</summary>
/// <remarks>Returns the key unchanged. Matching still uses default CLR value-tuple equality,
/// not provider collation, fixture identity comparers or reflection.</remarks>
public static class RepositoryCollectionKeys
{
    public static (T1, T2) Require<T1, T2>((T1, T2) key)
    {
        if (key.Item1 is null || key.Item2 is null) throw Invalid();
        return key;
    }

    public static (T1, T2, T3) Require<T1, T2, T3>((T1, T2, T3) key)
    {
        if (key.Item1 is null || key.Item2 is null || key.Item3 is null) throw Invalid();
        return key;
    }

    public static (T1, T2, T3, T4) Require<T1, T2, T3, T4>((T1, T2, T3, T4) key)
    {
        if (key.Item1 is null || key.Item2 is null || key.Item3 is null || key.Item4 is null) throw Invalid();
        return key;
    }

    public static (T1, T2, T3, T4, T5) Require<T1, T2, T3, T4, T5>((T1, T2, T3, T4, T5) key)
    {
        if (key.Item1 is null || key.Item2 is null || key.Item3 is null || key.Item4 is null || key.Item5 is null) throw Invalid();
        return key;
    }

    public static (T1, T2, T3, T4, T5, T6) Require<T1, T2, T3, T4, T5, T6>((T1, T2, T3, T4, T5, T6) key)
    {
        if (key.Item1 is null || key.Item2 is null || key.Item3 is null || key.Item4 is null || key.Item5 is null || key.Item6 is null) throw Invalid();
        return key;
    }

    public static (T1, T2, T3, T4, T5, T6, T7) Require<T1, T2, T3, T4, T5, T6, T7>((T1, T2, T3, T4, T5, T6, T7) key)
    {
        if (key.Item1 is null || key.Item2 is null || key.Item3 is null || key.Item4 is null || key.Item5 is null || key.Item6 is null || key.Item7 is null) throw Invalid();
        return key;
    }

    private static ArgumentException Invalid() => new("Collection matching key components must be non-null.");
}
