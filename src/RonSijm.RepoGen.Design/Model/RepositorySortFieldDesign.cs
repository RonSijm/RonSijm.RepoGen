using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositorySortFieldDesign
{
    internal RepositorySortFieldDesign(string name, LambdaExpression keySelector)
    {
        Name = name;
        KeySelector = keySelector;
    }

    public string Name { get; }
    public LambdaExpression KeySelector { get; internal set; }
}
