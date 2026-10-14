using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositorySortDesign
{
    internal RepositorySortDesign(LambdaExpression keySelector)
    {
        KeySelector = keySelector;
    }

    public LambdaExpression KeySelector { get; internal set; }

    public bool IsDescending { get; internal set; }
}
