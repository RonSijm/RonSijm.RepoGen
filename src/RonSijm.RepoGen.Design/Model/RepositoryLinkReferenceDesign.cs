using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicitly declared, immutable additional FK role on a reconciled link row.</summary>
public sealed class RepositoryLinkReferenceDesign
{
    internal RepositoryLinkReferenceDesign(LambdaExpression navigation, bool optional)
    {
        Navigation = navigation;
        Optional = optional;
    }

    public LambdaExpression Navigation { get; }
    public bool Optional { get; }
}
