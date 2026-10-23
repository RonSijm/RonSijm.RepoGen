namespace RonSijm.RepoGen.Design;

/// <summary>An immutable snapshot of explicitly configured reference annotations.</summary>
public sealed class RepositoryTypeNullabilityDesign
{
    internal RepositoryTypeNullabilityDesign(Type type, bool nullableReference, RepositoryTypeNullabilityDesign? element,
        RepositoryTypeNullabilityDesign[] arguments)
    {
        Type = type;
        NullableReference = nullableReference;
        ElementType = element;
        TypeArguments = Array.AsReadOnly(arguments);
    }

    public Type Type { get; }
    public bool NullableReference { get; }
    public RepositoryTypeNullabilityDesign? ElementType { get; }
    public IReadOnlyList<RepositoryTypeNullabilityDesign> TypeArguments { get; }
}
