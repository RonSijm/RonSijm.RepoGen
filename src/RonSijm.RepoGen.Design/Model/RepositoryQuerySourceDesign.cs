using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryQuerySourceDesign
{
    internal RepositoryQuerySourceDesign(LambdaExpression expression, Type resultType)
    {
        Expression = expression;
        ResultType = resultType;
    }

    public LambdaExpression Expression { get; internal set; }
    public Type ResultType { get; }
}
