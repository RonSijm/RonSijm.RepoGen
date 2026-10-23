using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicit comparer factory for one principal-key component in fixture relationships.</summary>
public sealed class RepositoryInMemoryRelationshipComparerDesign
{
    internal RepositoryInMemoryRelationshipComparerDesign(string propertyName, Type valueType, LambdaExpression comparer)
    {
        PropertyName = propertyName;
        ValueType = valueType;
        Comparer = comparer;
    }

    public string PropertyName { get; }
    public Type ValueType { get; }
    public LambdaExpression Comparer { get; }
}
