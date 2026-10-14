using System.Collections;

namespace RonSijm.RepoGen.Testing;

/// <summary>Structural or explicitly declared fixture relationship equality, never inferred database collation.</summary>
internal sealed class InMemoryRelationshipKey(object?[] parts, IEqualityComparer<object?[]>? comparer = null) : IEquatable<InMemoryRelationshipKey>
{
    private readonly object?[] parts = (object?[])parts.Clone();
    private readonly IEqualityComparer<object?[]>? comparer = comparer;
    public bool Equals(InMemoryRelationshipKey? other) => other is not null &&
        ReferenceEquals(comparer, other.comparer) && (comparer?.Equals(parts, other.parts) ?? StructuralComparisons.StructuralEqualityComparer.Equals(parts, other.parts));
    public override bool Equals(object? obj) => obj is InMemoryRelationshipKey other && Equals(other);
    public override int GetHashCode() => comparer?.GetHashCode(parts) ?? StructuralComparisons.StructuralEqualityComparer.GetHashCode(parts);
}
