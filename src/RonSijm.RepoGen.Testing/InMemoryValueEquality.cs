using System.Collections;

namespace RonSijm.RepoGen.Testing;

/// <summary>Ordinary scalar/binary equality for portable fixture writes; not SQL collation or EF custom comparison.</summary>
public static class InMemoryValueEquality
{
    public static bool AreEqual(object? left, object? right) => StructuralComparisons.StructuralEqualityComparer.Equals(left, right);
}
