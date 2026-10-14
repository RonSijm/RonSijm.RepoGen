using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryGroupedQueryDesign
{
    internal RepositoryGroupedQueryDesign(
        Type resultType,
        LambdaExpression keySelector,
        LambdaExpression projection)
    {
        ResultType = resultType;
        KeySelector = keySelector;
        Projection = projection;
    }

    public Type ResultType { get; }

    public LambdaExpression KeySelector { get; internal set; }

    public LambdaExpression Projection { get; internal set; }
}
