using System.Collections;

namespace RonSijm.RepoGen.Testing;

/// <summary>Explicit fixture relationship equality/hashing, component by component. Not SQL collation.</summary>
public sealed class InMemoryRelationshipKeyComparer : IEqualityComparer<object?[]>
{
    private readonly ComponentComparer?[] components;

    public InMemoryRelationshipKeyComparer(params ComponentComparer?[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length == 0) throw new ArgumentException("At least one relationship key component is required.", nameof(components));
        this.components = (ComponentComparer?[])components.Clone();
    }

    /// <summary>Adapts a typed comparer without reflection; null components retain the loader's no-match rule.</summary>
    public static ComponentComparer Component<T>(IEqualityComparer<T> comparer) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return new TypedComponent<T>(comparer);
    }

    public bool Equals(object?[]? x, object?[]? y)
    {
        if (x is not null) Validate(x);
        if (y is not null) Validate(y);
        if (x is null || y is null) return x is null && y is null;
        for (var index = 0; index < components.Length; index++)
        {
            var left = x[index]; var right = y[index];
            if (left is null || right is null)
            {
                if (left is not null || right is not null) return false;
            }
            else if (components[index] is { } component ? !component.AreEqual(left, right) :
                !StructuralComparisons.StructuralEqualityComparer.Equals(left, right)) return false;
        }
        return true;
    }

    public int GetHashCode(object?[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        Validate(obj);
        var hash = new HashCode();
        for (var index = 0; index < components.Length; index++)
            hash.Add(obj[index] is not { } value ? 0 : components[index]?.Hash(value) ?? StructuralComparisons.StructuralEqualityComparer.GetHashCode(value));
        return hash.ToHashCode();
    }

    internal void Validate(object?[] parts)
    {
        if (parts.Length != components.Length) throw new ArgumentException("Relationship comparer component count does not match the key shape.", nameof(parts));
        for (var index = 0; index < components.Length; index++)
            if (parts[index] is { } value) components[index]?.Validate(value);
    }

    public abstract class ComponentComparer
    {
        private protected ComponentComparer() { }
        internal abstract void Validate(object value);
        internal abstract bool AreEqual(object left, object right);
        internal abstract int Hash(object value);
    }

    private sealed class TypedComponent<T>(IEqualityComparer<T> comparer) : ComponentComparer where T : notnull
    {
        internal override void Validate(object value)
        {
            if (value is not T) throw new ArgumentException("Relationship key component must have type " + typeof(T).FullName + ".", nameof(value));
        }
        internal override bool AreEqual(object left, object right) => comparer.Equals((T)left, (T)right);
        internal override int Hash(object value) => comparer.GetHashCode((T)value);
    }
}
