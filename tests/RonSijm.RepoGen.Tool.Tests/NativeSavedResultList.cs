using System.Collections;

namespace RonSijm.RepoGen.Tool.Tests;

// Deliberately adversarial Count/enumeration behavior verifies the public runtime callback boundary.
internal sealed class NativeSavedResultList<T>(int count, IEnumerable<T> items) : IReadOnlyList<T>
{
    public int Count => count;
    public T this[int index] => throw new NotSupportedException("Only result enumeration should be used.");
    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
