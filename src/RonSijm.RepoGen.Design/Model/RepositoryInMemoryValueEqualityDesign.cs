using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Explicit fixture-only equality for one ordinary non-identity scalar.</summary>
public sealed class RepositoryInMemoryValueEqualityDesign
{
    internal RepositoryInMemoryValueEqualityDesign(string propertyName, LambdaExpression equality)
    {
        PropertyName = propertyName;
        Equality = equality;
    }

    public string PropertyName { get; }
    public LambdaExpression Equality { get; }
}
