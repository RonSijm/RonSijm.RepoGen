using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryFilterDesign
{
    internal RepositoryFilterDesign(string methodName, LambdaExpression predicate)
    {
        MethodName = methodName;
        Predicate = predicate;
    }

    public string MethodName { get; }

    public LambdaExpression Predicate { get; internal set; }

    public bool IsUnique { get; internal set; }
}
