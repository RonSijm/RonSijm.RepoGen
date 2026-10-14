using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryUpdateDesign
{
    internal RepositoryUpdateDesign(LambdaExpression property, LambdaExpression value, LambdaExpression? condition = null)
    {
        Property = property;
        Value = value;
        Condition = condition;
    }

    public LambdaExpression Property { get; internal set; }

    public LambdaExpression Value { get; internal set; }
    public LambdaExpression? Condition { get; internal set; }
}
