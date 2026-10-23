using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryScalarDesign
{
    internal RepositoryScalarDesign(LambdaExpression selector, Type returnType)
    {
        Selector = selector;
        ReturnType = returnType;
    }

    public LambdaExpression Selector { get; internal set; }
    public Type ReturnType { get; }
}
