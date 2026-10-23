using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A test-only value for one exact final-model scalar property, not a tracked-state adapter.</summary>
public sealed class RepositoryInMemoryPropertyDesign
{
    internal RepositoryInMemoryPropertyDesign(string propertyName, LambdaExpression value)
    {
        PropertyName = propertyName;
        Value = value;
    }

    public string PropertyName { get; }
    public LambdaExpression Value { get; }
}
