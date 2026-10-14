using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicit test-only translation of one closed method overload and fixed call shape.</summary>
public sealed class RepositoryInMemoryTranslationDesign
{
    internal RepositoryInMemoryTranslationDesign(string name, LambdaExpression call, LambdaExpression replacement, bool projection = false)
    {
        Name = name;
        Call = call;
        Replacement = replacement;
        IsProjection = projection;
    }

    public string Name { get; }
    public LambdaExpression Call { get; }
    public LambdaExpression Replacement { get; }
    /// <summary>True only for explicitly registered scalar-input, flat-DTO-result translations.</summary>
    public bool IsProjection { get; }
}
