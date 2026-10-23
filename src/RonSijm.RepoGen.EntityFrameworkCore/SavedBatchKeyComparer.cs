using System.Collections;

namespace RonSijm.RepoGen.EntityFrameworkCore;

internal sealed class SavedBatchKeyComparer : IEqualityComparer<object?[]>
{
    internal static SavedBatchKeyComparer Instance { get; } = new();
    public bool Equals(object?[]? x, object?[]? y) => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);
    public int GetHashCode(object?[] obj) => StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
}
