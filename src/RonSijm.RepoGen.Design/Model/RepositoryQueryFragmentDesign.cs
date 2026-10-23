using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryQueryFragmentDesign
{
    internal RepositoryQueryFragmentDesign(string name, LambdaExpression predicate)
    {
        Name = name;
        Predicate = predicate;
    }

    public string Name { get; }

    public LambdaExpression Predicate { get; internal set; }
}
