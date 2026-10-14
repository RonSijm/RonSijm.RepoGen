using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Explicit fixture value factory for one mapped generated scalar. Never changes production behavior.</summary>
public sealed class RepositoryInMemoryGeneratedValueDesign
{
    internal RepositoryInMemoryGeneratedValueDesign(string propertyName, LambdaExpression value)
    { PropertyName = propertyName; Value = value; }
    public string PropertyName { get; }
    public LambdaExpression Value { get; internal set; }
}
