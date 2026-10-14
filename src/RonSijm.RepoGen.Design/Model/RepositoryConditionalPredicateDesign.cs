using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryConditionalPredicateDesign
{
    internal RepositoryConditionalPredicateDesign(LambdaExpression condition, LambdaExpression predicate)
    {
        Condition = condition;
        Predicate = predicate;
    }

    public LambdaExpression Condition { get; internal set; }
    public LambdaExpression Predicate { get; internal set; }
}
