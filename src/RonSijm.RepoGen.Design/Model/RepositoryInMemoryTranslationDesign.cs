using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicit test-only translation of one closed method overload and fixed call shape.</summary>
public sealed class RepositoryInMemoryTranslationDesign
{
    internal RepositoryInMemoryTranslationDesign(string name, LambdaExpression call, LambdaExpression replacement,
        bool projection = false, bool allowNestedData = false)
    {
        Name = name;
        Call = call;
        Replacement = replacement;
        IsProjection = projection;
        AllowNestedData = allowNestedData;
    }

    public string Name { get; }
    public LambdaExpression Call { get; }
    public LambdaExpression Replacement { get; }
    /// <summary>True only for explicitly registered scalar-input, independent-DTO-result translations.</summary>
    public bool IsProjection { get; }
    /// <summary>Explicitly permits nested result DTOs and materialized arrays/lists; flat data remains the default.</summary>
    public bool AllowNestedData { get; }
}
