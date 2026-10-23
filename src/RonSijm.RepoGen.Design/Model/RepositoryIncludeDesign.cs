using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryIncludeDesign
{
    private readonly List<LambdaExpression> thenIncludes = new();

    internal RepositoryIncludeDesign(LambdaExpression navigation)
    {
        Navigation = navigation;
    }

    public LambdaExpression Navigation { get; internal set; }
    public IReadOnlyList<LambdaExpression> ThenIncludes => thenIncludes.AsReadOnly();
    internal List<LambdaExpression> ThenIncludesList => thenIncludes;
}
